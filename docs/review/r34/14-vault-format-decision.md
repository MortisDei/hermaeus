# 14. S3 fallback vault format: owner decision

**Status: CONDITIONALLY APPROVED FOR ISOLATED IMPLEMENTATION AND TESTS.** The
owner approved the v3 envelope, KDF, name authentication, fail-closed readers,
whole-vault migration, encrypted recovery snapshot and cooperating-writer
protocol, subject to the precise AAD encoding and relocation-safe recovery
locator specified below. Production activation remains disabled until passing
evidence and explicit owner acceptance of recovery and downgrade behaviour.
OS credential backends, settings references and key location stay unchanged.
No dependency or security framework is authorised.

## Inspected current behaviour

- [SecretStore](../../../src/Hermaeus.Services/SecretStore.cs) stores a JSON
  dictionary in `secrets.local.json` under Data Root. Keys are trimmed names,
  preserving case and Unicode; references remain `secret:<name>`. It tries the
  configured OS backend first, then the local fallback. A successful OS store
  removes that name from the local file.
- Current new fallback values are `v2:` plus Base64 of 16-byte salt, 16-byte
  AES-CBC IV and PKCS7 ciphertext. PBKDF2-SHA256 uses 100,000 iterations and
  32-byte output from the 32-byte local random key material. Older unsalted
  `v2:` values and unprefixed Base64 plaintext remain readable.
- CBC has no authentication tag. Strict UTF-8 decoding reduces accidental
  garbage acceptance but does not establish integrity. Current broad fallback
  attempts legacy decoding after a decrypt failure; that is unsuitable for v3.
- The key is Base64 in `secrets.local.key` in the separate user-specific
  protected directory, not an OS-encrypted key file. Unix mode restriction is
  best effort; Windows relies on the user-directory access boundary. Constructor
  logic attempts relocation of an old key from Data Root.
- Vault/key writes use a private temporary file then overwrite-move. There is
  no gate around vault read/modify/write or initial key creation. Two callers
  can lose updates or race key creation. Singleton DI is per process, not a
  cross-process guarantee. Missing key reads can currently create a new key.
- [BackupService](../../../src/Hermaeus.Services/BackupService.cs) excludes the
  vault and key by filename. Data-root migration moves the encrypted vault but
  retains the separate protected key. A new recovery artifact must not become
  an ordinary Data Root backup attachment.

Existing registered harness cases in
[ServiceTests](../../../src/Hermaeus.Tests/ServiceTests.cs) exercise fallback
round-trip/no literal plaintext, fresh ciphertext, atomic key temp cleanup,
Unix key mode, warning on failed decrypt, legacy-key relocation and copied-root
failure without the original key. `BackupMigrationTests` covers resolution
after a Data Root move. These do **not** prove authenticated encryption,
concurrent writer correctness, crash recovery or downgrade compatibility.
No tests were executed for this decision document.

The proposed nonce/tag sizes and explicit tag-size constructor use the existing
[.NET AesGcm API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0).
API availability is not migration or recovery evidence.

## Recommended format, fixed by approval

Keep the dictionary and `secret:` reference contract. Each new fallback value
is ASCII `v3:` followed by ordinary padded Base64 of this binary layout:

| Decoded offset | Length | Field |
| --- | --- | --- |
| 0 | 16 bytes | Cryptographically random salt |
| 16 | 12 bytes | Cryptographically random GCM nonce |
| 28 | 16 bytes | Authentication tag |
| 44 | Remaining bytes | Ciphertext of the secret's strict UTF-8 bytes |

The prefix fixes the algorithm and lengths; there are no optional algorithms,
variable tag sizes, caller-selected iteration counts or internal length fields.
Generate fresh salt and nonce with `RandomNumberGenerator` on **every** write,
even for an unchanged secret. Derive the 32-byte AES key using built-in
PBKDF2-SHA256, 100,000 iterations, with existing key material and KDF salt:

```text
ASCII("Hermaeus.SecretStore.v3\0") || salt[16]
```

Use built-in `AesGcm` with an explicitly specified 16-byte tag. For a value,
authenticate this additional data (AAD), without encrypting the name:

```text
ASCII("Hermaeus.SecretStore\0v3\0value\0")
|| UInt32LittleEndian(nameUtf8.Length)
|| nameUtf8
```

`nameUtf8` is the strict UTF-8 encoding, without a BOM, of the **exact normalised
dictionary key**. The length is its byte count, encoded as exactly four bytes
of unsigned UInt32 little-endian. It is not the UTF-16 character count or the
length of the unnormalised input. Invalid UTF-16 input is rejected rather than
encoded with replacement characters. All ASCII literals above include the
explicit NUL bytes shown and no implicit terminator. Normalisation remains
trim/default-name behaviour, with ordinal case and no Unicode composition
folding. Existing dictionary keys must already equal their normalised form;
reject a malformed dictionary instead of silently renaming keys or merging
normalisation collisions. Do not bind to Data Root, hostname or executable
version: moving an intact vault with its existing protected key must work.
Name binding rejects moving a ciphertext to another dictionary key. It does
not prevent replay of an older valid value under the **same** name/key, deletion
of entries, or replacement with an older authentic vault. No anti-rollback
counter or new authority is proposed.

Legacy compatibility is a further limit: replacing an entire dictionary value
with a syntactically valid legacy value is outside v3 authentication. A reader
that still accepts legacy values cannot prove that such a replacement was
authorised. This proposal authenticates recognised v3 payloads, not the complete
vault or its format history. If the owner requires downgrade-resistant whole
vault integrity, stop and review a separate authenticated vault envelope;
do not imply that the unchanged dictionary supplies it.

Recognise the version prefix case-insensitively, as existing v2 readers do;
writers emit lowercase `v3:`. Decode recognised v3 only through this parser.
Invalid Base64, fewer than
44 decoded bytes, authentication failure, invalid UTF-8, missing/wrong/corrupt
key or unsupported GCM must return unavailable with a fixed redacted diagnostic.
Never try CBC or Base64 plaintext after recognising v3. Reject unknown numeric
version prefixes as unsupported rather than treating them as legacy plaintext.
Continue explicit legacy readers for salted/unsalted v2 and unprefixed values;
reads do not rewrite or silently repair the vault. An unreadable existing vault
must not cause replacement key creation. A new empty vault, or a validated
dictionary containing only unprefixed legacy plaintext values, may create a
key under the ownership protocol below. Any v2/v3 entry requires its original
key. Reuse the existing key; do not rotate
or relocate it as part of S3.

## One isolated migration transaction

**Recommendation:** migrate the complete local dictionary on its first
fallback mutation after format approval. Reads remain non-mutating. Avoid
background migration, lazy per-read writes, dual-write v2/v3, or an additional
live credential store. Both fallback Store and local removal after an OS-store
success use the same mutation transaction. Empty-vault deletion must preserve
the existing key and any retained recovery snapshot.

1. Obtain exclusive cooperating-writer ownership, then capture the effective
   vault path and existing key. Re-read the vault inside that ownership. Validate
   every value through its explicit reader before touching the original file.
   If any value cannot be read, abort the complete mutation and retain the exact
   original. Never silently omit an unreadable entry.
2. If legacy entries exist, prepare one **encrypted** pre-migration snapshot
   from the exact original JSON bytes. Unprefixed Base64 values may contain
   plaintext secrets, so copying the old JSON directly to a backup is forbidden.
   Put the snapshot only under the existing protected directory, outside Data
   Root; do not include the key or plaintext in it.
   The inactive transaction now refuses a configured protected directory equal
   to or beneath Data Root before file creation, using the existing platform
   path comparison. This does not change key location or add filesystem identity
   authority. Later backup/privacy evidence is in [17](17-backup-credential-artifact-privacy.md).
3. The snapshot uses the same `v3:` binary envelope/KDF, fresh salt/nonce and
   AAD `ASCII("Hermaeus.SecretStore\0v3\0snapshot\0") || vaultId[32]`.
   `vaultId` is SHA256 of strict UTF-8 of the fully resolved vault path, with
   Windows case folded invariantly and Linux case preserved. Its name is
   `<lowercase-vaultId-hex>.pre-v3.snapshot`. Path identity here locates the
   recovery artifact; ordinary value AAD remains path independent. Keep this
   original ID after Data Root relocation through the protected locator below.
4. Create/flush/authenticate the snapshot and its locator before committing v3.
   Keep both immutable. An existing matching snapshot can be reused only after decrypting
   and byte-comparing it to the original being migrated. A different existing
   snapshot or locator blocks migration until the owner explicitly retires or
   replaces it; never overwrite the only recovery point. Retain at most that one snapshot
   per vault identity until the owner approves deletion.
5. Apply the requested change to the decrypted in-memory dictionary. Encrypt
   every surviving value as v3. Stage the complete JSON beside the primary
   vault, restrict permissions before exposure, flush file content, close, then
   atomically replace the primary through the established file-write pattern.
   Validate/reopen the committed dictionary before reporting success. No
   plaintext migration temporary file or additional mutable manifest is needed.
6. Honour cancellation before the commit point. Once replacement starts,
   finish the bounded commit/reopen outcome rather than reporting an untouched
   cancellation. Clean only transaction-owned staging files. A post-commit
   error must not automatically restore an old snapshot over newer writes.

Atomic rename is a commit boundary, **not** a universal power-loss durability
promise. File flushing, filesystem behaviour, error injection and reopen must
be tested on both supported hosts. A crash before replacement leaves the old
primary authoritative; a valid snapshot alone does not mean migration committed.
A valid v3 primary remains authoritative after replacement. If the primary is
corrupt or absent unexpectedly, fail closed and request explicit recovery;
never auto-select a stale snapshot or regenerate a key.

## Protected recovery locator and relocation

Use one immutable locator per snapshot, in the same existing protected directory:
`<lowercase-original-vaultId-hex>.pre-v3.locator`. Its entire contents are ASCII
`v3:` plus the same binary envelope and KDF above, with fresh salt and nonce.
Its plaintext is exactly 32 bytes: SHA256 of the **complete snapshot file bytes**,
including the snapshot's ASCII prefix and Base64 encoding. Its AAD is exactly:

```text
ASCII("Hermaeus.SecretStore\0v3\0locator\0") || originalVaultId[32]
```

The original vault ID comes from the fixed 64-character lowercase hexadecimal
basename, not a mutable path field. No arbitrary snapshot path, current-root
pointer, timestamp ordering, plaintext key or secret name is persisted. Locator
and snapshot names are constructed locally from a validated ID and resolved
only inside the protected directory, with symlink/reparse rejection. Check
exact envelope lengths before reading or decrypting. The locator's decoded
envelope is exactly 76 bytes. Hash snapshot contents incrementally before
allocating decrypted data; reject impossible/overflowing envelope or array
lengths. A new user-visible vault size limit needs a separately justified owner
decision; the locator must not silently introduce one.

Under the same writer lock, stage, restrict, flush and atomically publish the
snapshot first, then its locator; authenticate both and check the digest before
replacing the primary. Never overwrite either existing immutable artefact.
An existing complete pair is reusable only if the authenticated snapshot
decrypts to the exact current pre-migration JSON bytes. A crash leaving only a
snapshot can complete the locator on retry only after that same exact-byte
check. A locator without its snapshot, a mismatched digest, or an unreadable
artefact blocks mutation and requires explicit investigation. Creating a
locator from an arbitrary discovered snapshot is not a recovery operation.

Discovery enumerates only these fixed locator basenames in the protected
directory, validates each pair using the original ID, and presents the original
IDs for **explicit selection**. Enumeration has a fixed bounded candidate
count; exceeding it reports that explicit ID lookup is required, without
silently truncating the result. A corrupt candidate is reported as unavailable,
never substituted with another candidate. Do not select by newest timestamp,
current Data Root, apparent similarity or first successful decryption. Moving
the vault to a new Data Root leaves the protected pair and its original ID
unchanged, so recovery discovery does not depend on remembering the old path
or modifying Data Root migration. A copied vault without the original protected
key and recovery pair has no implied recovery capability.

Recovery requires explicit selection of an authenticated original ID and review
of the current target primary. Capture the target's exact SHA256, or explicit
absence, together with the selected locator and snapshot digests. Immediately
before any restore, reacquire writer ownership and recheck all three artefacts
and the captured target state. Any difference aborts restoration and requires
fresh review. Refuse malformed or unreviewed target paths and any active Data
Root transition. Successful restore is an explicit atomic replacement of that
reviewed target only, with the documented loss of later fallback changes.
An authenticated snapshot is never permission to overwrite newer credentials
automatically. No automatic restoration or automatic downgrade is authorised.

## Concurrency and recovery ownership

Use a small, SecretStore-local ownership protocol built from platform/.NET
facilities, not a new vault framework. Proposed protocol is one cooperating
exclusive file handle (`FileShare.None`) on `secrets.local.lock` in the existing
protected directory, plus an in-process async gate. The protected-directory
gate covers initial key creation, legacy-key relocation and every fallback
read/modify/write across vaults sharing that key. Lock acquisition must be
cancellable and bounded, with no deletion of a lock owned by another process.
An instance-only semaphore is insufficient. Prove the handle semantics with
two real processes on Windows/Linux before selecting this primitive; if that
proof fails, bring a concrete alternative back to the owner.

Move constructor-side key writes into the guarded fallback operation rather
than allowing migration to race key creation. Existing valid key material must
never be overwritten by a concurrent creator. Capture the effective root once
per operation and coordinate Data Root restart/transition through existing
owners. OS-backend credential operations remain unchanged, but successful
OS-store/local-removal races require regression coverage. Do not hold plaintext
in logs, exception text, diagnostics or a lock filename.

Old builds do not participate in a new lock protocol. Stop all old/new writers
before migration, recovery or downgrade. This is cooperating ownership, not a
security boundary against another process running as the same user. Check
private directory/file permissions and reject reparse/symlink redirection before
staging a snapshot or replacing key/vault artifacts. Do not claim Windows ACL
or Unix permission guarantees from the current best-effort helper alone.

## Downgrade and reversibility limits

An old build cannot read v3. **No automatic downgrade or v2 dual-write is
proposed.** With all writers stopped, the approved new build's recovery path
can authenticate/decrypt the snapshot using the **same original protected
key**, validate its old dictionary, and atomically restore those original JSON
bytes. The older binary can then read only the formats it already understood.
The recovery capability must be implemented and proven before enabling migration.

Restoring that snapshot discards every fallback secret added, changed or removed
after it was taken. Settings may still reference a post-migration name absent
from the snapshot; those credentials need re-entry. OS credential-manager
changes are outside this snapshot and are not rolled back. Revoked or rotated
provider keys may no longer work even after local restoration. Losing the
protected key makes both the v3 vault and encrypted snapshot unrecoverable.
The snapshot is a recovery point, not a continuously current backup. This is
conditional recovery, not a promise of lossless reversibility.

## Required proof before activation

Explicit restoration of original legacy JSON necessarily materialises that old
format, which may contain unprefixed Base64 plaintext values. Its private staging
and restored primary therefore rely on the private file/directory boundary;
this is part of the downgrade privacy limitation, not a v3 migration snapshot.

A crash after first-key publication but before first-primary publication leaves
a key-only state. An absent primary with an existing key, or an existing empty
primary with a missing key, is refused rather than silently initialised. Any
protected recovery history also prevents replacement-key creation. Empty v3
vaults retain `{}`. No automatic recovery or new-vault initialisation bypass is
implemented for these ambiguous states; their explicit investigation workflow
remains an activation acceptance item.

- Exact binary/AAD fixtures, Unicode names/secrets, fresh salt/nonce, all three
  legacy forms, fresh reopen, copied root with/without original key, and normal
  Data Root migration. Preserve reference and OS-backend behaviour.
- Every salt/nonce/tag/ciphertext bit class, name substitution, truncation,
  malformed/unknown prefix, wrong/missing/corrupt key and unavailable algorithm
  fail closed with no legacy fallthrough or credential-bearing diagnostic.
- Concurrent same/different names, Store versus local removal, two instances,
  two processes, first-key creation, migration versus save and cancellation at
  acquisition/read/stage/commit boundaries. No lost update or key replacement.
- Fault injection before/after snapshot flush, primary staging/flush/replacement,
  reopen and cleanup; corrupt/existing snapshot, disk-full/access-denied paths,
  pending Data Root transition and process interruption. Verify actual retained
  files and reopen behaviour, not just a returned status.
- Restore an encrypted snapshot after later add/update/delete operations and
  prove the documented loss explicitly; old-build compatibility against the
  restored bytes, original key requirement, backup exclusion of all secret
  artifacts, redaction and permission checks. New harness cases remain registered.
- Discover and explicitly select the original snapshot after Data Root relocation,
  including multiple valid older snapshots. Verify snapshot-only interruption,
  locator-only/corrupt/mismatched pairs, bounded discovery overflow, symlink or
  reparse redirection, and a target changed between review and restore. None may
  trigger automatic selection, overwrite of a changed primary or key creation.
- Full repository verification and isolated native fallback/OS-backend checks
  on Windows/Linux. No real credentials in retained fixtures or captures.

## Owner decision and activation gate

The owner conditionally approved isolated implementation and tests, with the
mandatory exact AAD encoding and relocation-safe locator corrections recorded
above. Approval preserves key location, OS credential backends and secret
references. It does not authorise production activation, a dependency, a new
security framework, automatic downgrade, legacy fallthrough after recognised
v3 failure or automatic snapshot restoration. Any material security-design
change requires a further owner decision. Format activation waits for the
required automated and native evidence plus explicit owner acceptance of
recovery and downgrade behaviour.
