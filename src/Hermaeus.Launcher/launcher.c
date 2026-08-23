#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define PATH_CAPACITY 32768

static WCHAR self_path[PATH_CAPACITY];
static WCHAR target_path[PATH_CAPACITY];
static WCHAR working_directory[PATH_CAPACITY];
static WCHAR command_line[PATH_CAPACITY];
static WCHAR failure_message[1024];
static STARTUPINFOW startup_info;
static PROCESS_INFORMATION process_info;

static SIZE_T StringLength(const WCHAR* value)
{
    SIZE_T length = 0;
    while (value[length] != L'\0')
        length++;
    return length;
}

static BOOL CopyString(WCHAR* destination, SIZE_T capacity, const WCHAR* source)
{
    SIZE_T index = 0;
    while (source[index] != L'\0')
    {
        if (index + 1 >= capacity)
            return FALSE;
        destination[index] = source[index];
        index++;
    }

    destination[index] = L'\0';
    return TRUE;
}

static BOOL AppendString(WCHAR* destination, SIZE_T capacity, const WCHAR* suffix)
{
    SIZE_T destination_length = StringLength(destination);
    SIZE_T suffix_index = 0;
    while (suffix[suffix_index] != L'\0')
    {
        if (destination_length + suffix_index + 1 >= capacity)
            return FALSE;
        destination[destination_length + suffix_index] = suffix[suffix_index];
        suffix_index++;
    }

    destination[destination_length + suffix_index] = L'\0';
    return TRUE;
}

static void ShowFailure(const WCHAR* summary, DWORD error_code)
{
    failure_message[0] = L'\0';
    CopyString(failure_message, 1024, summary);

    if (error_code != ERROR_SUCCESS)
    {
        AppendString(failure_message, 1024, L"\r\n\r\n");
        FormatMessageW(
            FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            NULL,
            error_code,
            0,
            failure_message + StringLength(failure_message),
            (DWORD)(1024 - StringLength(failure_message)),
            NULL);
    }

    MessageBoxW(NULL, failure_message, L"Hermaeus", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
}

static int LaunchHermaeus(void)
{
    const WCHAR* target_suffix = L"\\app\\Hermaeus.Desktop.exe";
    const WCHAR* working_suffix = L"\\app";
    DWORD self_length = GetModuleFileNameW(NULL, self_path, PATH_CAPACITY);
    if (self_length == 0 || self_length >= PATH_CAPACITY)
    {
        DWORD error_code = self_length == 0 ? GetLastError() : ERROR_INSUFFICIENT_BUFFER;
        ShowFailure(L"Hermaeus could not resolve the package location.", error_code);
        return 1;
    }

    SIZE_T separator = self_length;
    while (separator > 0 && self_path[separator - 1] != L'\\' && self_path[separator - 1] != L'/')
        separator--;
    if (separator == 0)
    {
        ShowFailure(L"Hermaeus could not resolve the package directory.", ERROR_INVALID_NAME);
        return 1;
    }

    self_path[separator - 1] = L'\0';
    if (!CopyString(target_path, PATH_CAPACITY, self_path)
        || !AppendString(target_path, PATH_CAPACITY, target_suffix)
        || !CopyString(working_directory, PATH_CAPACITY, self_path)
        || !AppendString(working_directory, PATH_CAPACITY, working_suffix))
    {
        ShowFailure(L"The Hermaeus package path is too long.", ERROR_FILENAME_EXCED_RANGE);
        return 1;
    }

    DWORD attributes = GetFileAttributesW(target_path);
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
    {
        ShowFailure(
            L"The bundled application is missing. Expected app\\Hermaeus.Desktop.exe beside this launcher.",
            attributes == INVALID_FILE_ATTRIBUTES ? GetLastError() : ERROR_FILE_NOT_FOUND);
        return 1;
    }

    const WCHAR* original_command_line = GetCommandLineW();
    if (original_command_line == NULL || !CopyString(command_line, PATH_CAPACITY, original_command_line))
    {
        ShowFailure(L"The command line is too long to forward to Hermaeus.", ERROR_FILENAME_EXCED_RANGE);
        return 1;
    }

    startup_info.cb = sizeof(startup_info);

    if (!CreateProcessW(
            target_path,
            command_line,
            NULL,
            NULL,
            FALSE,
            0,
            NULL,
            working_directory,
            &startup_info,
            &process_info))
    {
        ShowFailure(L"Hermaeus could not start the bundled application.", GetLastError());
        return 1;
    }

    CloseHandle(process_info.hThread);
    CloseHandle(process_info.hProcess);
    return 0;
}

void WINAPI wWinMainCRTStartup(void)
{
    ExitProcess((UINT)LaunchHermaeus());
}
