---
name: screenshot-verification
description: Capture and inspect real packaged desktop screenshots on Linux/COSMIC or Windows when visual acceptance is in scope, while keeping pixel evidence separate from interaction and runtime proof.
---

# Screenshot verification

Use this skill when a real desktop screenshot is part of an acceptance claim.
Run the exact packaged or native target in the owner-approved session, record
the package/build identity and platform session, capture only after the window
is visible, and inspect the pixels before classifying the result.

A screenshot proves the visible pixels at one instant. It can establish window
identity, visible navigation, model or status text, layout, and obvious visual
defects. It does not prove clicks, keyboard focus, dialogs, accessibility,
DPI or resize behavior, editor input, model execution, child-process cleanup,
or clean shutdown. Keep those as separate evidence receipts and keep every
unproven GUI row as `NEEDS OWNER VALIDATION`.

For Linux/COSMIC, record whether the screenshot came from Wayland, XWayland,
or another session and note other visible windows or overlays that could
obscure the product. For Windows, capture the packaged app in the target
desktop session and inspect title/icon identity, navigation, readable status,
window bounds, scaling, and clipping. A Windows screenshot has the same
evidence limits as a Linux screenshot.

Recorded Hermaeus receipt: the 2026-09-13 approved-host Linux/COSMIC package
launch produced a real Hermaeus window showing the model selector, navigation
bar, conversation list, and a Doctor banner reading `0 errors and 2 warnings`.
The temporary screenshot was pixel-inspected and then removed. Native CUA
exposed no app/window interaction controls, so this receipt did not close model
switching, Lab or Benchmark walkthroughs, editor or approval checks, or clean
window-close acceptance.

Keep captures outside the repository unless they are deliberately retained as
review artifacts. Do not treat an unavailable computer-use surface, a source
render test, or a screenshot of an unrelated window as a product GUI pass.
