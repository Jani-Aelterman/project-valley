#include <windows.h>

#define WM_NEXTVALLEY_UNHOOK (WM_APP + 1)

// Global state
WNDPROC g_OldWndProc = NULL;
HANDLE g_hPipe = INVALID_HANDLE_VALUE;
HMODULE g_hModule = NULL;
CRITICAL_SECTION g_PipeCS;
volatile LONG g_Detaching = 0;
HANDLE g_hUnhookDoneEvent = NULL;

#define MAX_ICON_WIDTH 256
#define MAX_ICON_HEIGHT 256

#pragma pack(push, 1)
struct PipeMessageHeader {
    DWORD type; // 1 = text, 2 = COPYDATA
};

struct PipeCopyDataMessage {
    PipeMessageHeader header;
    DWORDLONG dwData;
    DWORD cbData; // size of NOTIFYICONDATA payload
    DWORD iconWidth;
    DWORD iconHeight;
    DWORD iconDataSize; // 0 = no icon, >0 = RGBA bytes follow
};

struct NOTIFYICONDATA32 {
    DWORD cbSize;
    DWORD hWnd;
    DWORD uID;
    DWORD uFlags;
    DWORD uCallbackMessage;
    DWORD hIcon;
    WCHAR szTip[128];
    DWORD dwState;
    DWORD dwStateMask;
    WCHAR szInfo[256];
    union {
        UINT uTimeout;
        UINT uVersion;
    } DUMMYUNIONNAME;
    WCHAR szInfoTitle[64];
    DWORD dwInfoFlags;
    GUID guidItem;
    DWORD hBalloonIcon;
};

struct SHELLTRAYDATA {
    DWORD dwSignature;
    DWORD dwMessage;
    NOTIFYICONDATA32 nid;
};
#pragma pack(pop)

void ConnectToPipe() {
    EnterCriticalSection(&g_PipeCS);
    if (g_hPipe == INVALID_HANDLE_VALUE) {
        g_hPipe = CreateFileW(L"\\\\.\\pipe\\nextvalley_tray_monitor", GENERIC_WRITE, 0, NULL, OPEN_EXISTING,
                              FILE_FLAG_OVERLAPPED, NULL);
    }
    LeaveCriticalSection(&g_PipeCS);
}

bool ExtractIconRGBA(HICON hIcon, BYTE *&outRGBA, DWORD &outSize, DWORD &outWidth, DWORD &outHeight) {
    ICONINFO iconInfo = {};
    if (!GetIconInfo(hIcon, &iconInfo))
        return false;

    BITMAP bitmap = {};
    if (!GetObject(iconInfo.hbmColor, sizeof(bitmap), &bitmap)) {
        DeleteObject(iconInfo.hbmMask);
        DeleteObject(iconInfo.hbmColor);
        return false;
    }

    outWidth = (DWORD)bitmap.bmWidth;
    outHeight = (DWORD)bitmap.bmHeight;

    if (outWidth == 0 || outHeight == 0 || outWidth > MAX_ICON_WIDTH || outHeight > MAX_ICON_HEIGHT) {
        DeleteObject(iconInfo.hbmMask);
        DeleteObject(iconInfo.hbmColor);
        return false;
    }

    BITMAPINFOHEADER bitmapInfo = {};
    bitmapInfo.biSize = sizeof(bitmapInfo);
    bitmapInfo.biWidth = outWidth;
    bitmapInfo.biHeight = -(LONG)outHeight;
    bitmapInfo.biPlanes = 1;
    bitmapInfo.biBitCount = 32;
    bitmapInfo.biCompression = BI_RGB;

    DWORD pixelCount = outWidth * outHeight;
    outSize = pixelCount * 4;

    outRGBA = (BYTE *)HeapAlloc(GetProcessHeap(), 0, outSize);
    if (!outRGBA) {
        DeleteObject(iconInfo.hbmMask);
        DeleteObject(iconInfo.hbmColor);
        return false;
    }

    HDC hdc = CreateCompatibleDC(NULL);
    BOOL ok = GetDIBits(hdc, iconInfo.hbmColor, 0, outHeight, outRGBA, (BITMAPINFO *)&bitmapInfo, DIB_RGB_COLORS) ==
              (int)outHeight;

    bool isMaskBased = true;
    for (DWORD i = 0; i < pixelCount; i++) {
        if (outRGBA[i * 4 + 3] != 0) {
            isMaskBased = false;
            break;
        }
    }

    BYTE *maskBytes = NULL;
    BOOL maskOk = FALSE;
    if (isMaskBased && iconInfo.hbmMask) {
        maskBytes = (BYTE *)HeapAlloc(GetProcessHeap(), 0, outSize);
        if (maskBytes) {
            maskOk = GetDIBits(hdc, iconInfo.hbmMask, 0, outHeight, maskBytes, (BITMAPINFO *)&bitmapInfo,
                               DIB_RGB_COLORS) == (int)outHeight;
        }
    }
    DeleteDC(hdc);

    if (ok) {
        for (DWORD i = 0; i < pixelCount; i++) {
            BYTE b = outRGBA[i * 4 + 0];
            BYTE g = outRGBA[i * 4 + 1];
            BYTE r = outRGBA[i * 4 + 2];
            BYTE a = outRGBA[i * 4 + 3];

            if (isMaskBased) {
                if (maskOk && maskBytes) {
                    a = (maskBytes[i * 4] == 255) ? 0 : 255;
                } else {
                    a = 255; // mask fetch failed, assume fully opaque
                }
            }

            // Premultiply alpha? Usually WPF/WinUI expects PBGRA or BGRA. Let's send raw BGRA.
            outRGBA[i * 4 + 0] = b;
            outRGBA[i * 4 + 1] = g;
            outRGBA[i * 4 + 2] = r;
            outRGBA[i * 4 + 3] = a;
        }
    } else {
        HeapFree(GetProcessHeap(), 0, outRGBA);
        outRGBA = NULL;
        outSize = 0;
        ok = FALSE;
    }

    if (maskBytes)
        HeapFree(GetProcessHeap(), 0, maskBytes);

    DeleteObject(iconInfo.hbmMask);
    DeleteObject(iconInfo.hbmColor);
    return ok;
}

void InternalWriteToPipe(void *buffer, DWORD totalSize) {
    ConnectToPipe();

    EnterCriticalSection(&g_PipeCS);
    if (g_hPipe != INVALID_HANDLE_VALUE) {
        DWORD written;
        OVERLAPPED overlapped = {0};
        overlapped.hEvent = CreateEvent(NULL, TRUE, FALSE, NULL);

        if (overlapped.hEvent) {
            if (!WriteFile(g_hPipe, buffer, totalSize, &written, &overlapped)) {
                if (GetLastError() == ERROR_IO_PENDING) {
                    if (WaitForSingleObject(overlapped.hEvent, 500) != WAIT_OBJECT_0) {
                        CancelIo(g_hPipe);
                        CloseHandle(g_hPipe);
                        g_hPipe = INVALID_HANDLE_VALUE;
                    }
                } else {
                    CloseHandle(g_hPipe);
                    g_hPipe = INVALID_HANDLE_VALUE;
                }
            }
            CloseHandle(overlapped.hEvent);
        }
    }
    LeaveCriticalSection(&g_PipeCS);
}

void SendTextToPipe(const char *msg) {
    size_t msgLen = strlen(msg);
    size_t totalSize = sizeof(PipeMessageHeader) + msgLen;
    char *buffer = (char *)malloc(totalSize);
    if (buffer) {
        PipeMessageHeader header = {1}; // 1 = text
        memcpy(buffer, &header, sizeof(header));
        memcpy(buffer + sizeof(header), msg, msgLen);
        InternalWriteToPipe(buffer, (DWORD)totalSize);
        free(buffer);
    }
}

void SendCopyDataToPipe(PCOPYDATASTRUCT pcds) {
    if (!pcds)
        return;

    BYTE *iconRGBA = NULL;
    DWORD iconSize = 0, iconWidth = 0, iconHeight = 0;

    SHELLTRAYDATA *trayData = (SHELLTRAYDATA *)pcds->lpData;
    if (!trayData) {
        return;
    }

    NOTIFYICONDATA32 *nid = &trayData->nid;
    if (nid && (nid->uFlags & NIF_ICON) && nid->hIcon) {
        HICON hIconCopy = CopyIcon((HICON)(ULONG_PTR)nid->hIcon);
        if (hIconCopy) {
            ExtractIconRGBA(hIconCopy, iconRGBA, iconSize, iconWidth, iconHeight);
            DestroyIcon(hIconCopy);
        }
    }

    PipeCopyDataMessage msg = {};
    msg.header.type = 2;
    msg.dwData = pcds->dwData;
    msg.cbData = (DWORD)pcds->cbData;
    msg.iconWidth = iconWidth;
    msg.iconHeight = iconHeight;
    msg.iconDataSize = iconSize;

    size_t totalSize = sizeof(msg) + msg.cbData + msg.iconDataSize;
    char *buffer = (char *)malloc(totalSize);
    if (buffer) {
        char *cursor = buffer;
        memcpy(cursor, &msg, sizeof(msg));
        cursor += sizeof(msg);
        if (msg.cbData > 0 && pcds->lpData) {
            memcpy(cursor, pcds->lpData, msg.cbData);
            cursor += msg.cbData;
        }
        if (msg.iconDataSize > 0) {
            memcpy(cursor, iconRGBA, msg.iconDataSize);
        }

        InternalWriteToPipe(buffer, (DWORD)totalSize);
        free(buffer);
    }

    if (iconRGBA) {
        HeapFree(GetProcessHeap(), 0, iconRGBA);
    }
}

void DebugOutput(const char *msg) {
    SendTextToPipe(msg);
    OutputDebugStringA(msg);
}

HWND FindRealSystray() {
    HWND hRealTray = NULL;
    WORD tries = 0;

    while (true) {
        hRealTray = FindWindowExW(0, hRealTray, L"Shell_TrayWnd", NULL);
        if (hRealTray == NULL || hRealTray == INVALID_HANDLE_VALUE) {
            if (tries > 20) {
                DebugOutput("[DLL] Failed to find real systray window. Giving up.\n");
                break;
            }
            tries++;
            char buf[256];
            wsprintfA(buf, "[DLL] Failed to find real systray window. Retrying... %d\n", tries);
            DebugOutput(buf);
            Sleep(50);
            continue;
        }

        DWORD pid;
        GetWindowThreadProcessId(hRealTray, &pid);
        if (pid != GetCurrentProcessId()) {
            continue;
        }

        return hRealTray;
    }
    return 0;
}

LRESULT CALLBACK ManualSubclassProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    WNDPROC oldProc = g_OldWndProc;

    if (uMsg == WM_NEXTVALLEY_UNHOOK) {
        if (oldProc) {
            SetWindowLongPtrW(hWnd, GWLP_WNDPROC, (LONG_PTR)oldProc);
            g_OldWndProc = NULL;
        }
        if (g_hUnhookDoneEvent) {
            SetEvent(g_hUnhookDoneEvent);
        }
        return 0;
    }

    if (uMsg == WM_NCDESTROY) {
        if (oldProc) {
            SetWindowLongPtrW(hWnd, GWLP_WNDPROC, (LONG_PTR)oldProc);
            g_OldWndProc = NULL;
        }
        EnterCriticalSection(&g_PipeCS);
        if (g_hPipe != INVALID_HANDLE_VALUE) {
            CloseHandle(g_hPipe);
            g_hPipe = INVALID_HANDLE_VALUE;
        }
        LeaveCriticalSection(&g_PipeCS);
    }

    if (!g_Detaching && uMsg == WM_COPYDATA) {
        PCOPYDATASTRUCT pcds = (PCOPYDATASTRUCT)lParam;
        if (pcds && pcds->dwData == 1) {
            SendCopyDataToPipe(pcds);
        }
    }

    return CallWindowProc(oldProc, hWnd, uMsg, wParam, lParam);
}

DWORD WINAPI WatchdogThread(LPVOID lpParam) {
    HANDLE hMutex = OpenMutexW(SYNCHRONIZE, FALSE, L"Global\\NextValleyTrayHookAlive");
    if (!hMutex) {
        DebugOutput("[DLL] Watchdog: host mutex not found, self-detaching.\n");
    } else {
        DebugOutput("[DLL] Watchdog active. Sleeping until host drops.\n");
        DWORD result = WaitForSingleObject(hMutex, INFINITE);

        if (result == WAIT_ABANDONED || result == WAIT_OBJECT_0) {
            DebugOutput("[DLL] Watchdog: Host gone, self-detaching.\n");
        } else {
            DebugOutput("[DLL] Watchdog: wait failed/lost, self-detaching.\n");
        }
        CloseHandle(hMutex);
    }

    InterlockedExchange(&g_Detaching, 1);
    OutputDebugStringA("[DLL] Detach: g_Detaching set.\n");

    HWND hTray = FindRealSystray();
    if (hTray && g_OldWndProc) {
        OutputDebugStringA("[DLL] Detach: PostMessage WM_NEXTVALLEY_UNHOOK...\n");
        PostMessageW(hTray, WM_NEXTVALLEY_UNHOOK, 0, 0);
        DWORD waitResult = WaitForSingleObject(g_hUnhookDoneEvent, 5000);
    } else {
        OutputDebugStringA("[DLL] Detach: No tray or wndproc to unhook.\n");
    }

    EnterCriticalSection(&g_PipeCS);
    if (g_hPipe != INVALID_HANDLE_VALUE) {
        FlushFileBuffers(g_hPipe);
        CloseHandle(g_hPipe);
        g_hPipe = INVALID_HANDLE_VALUE;
    }
    LeaveCriticalSection(&g_PipeCS);
    OutputDebugStringA("[DLL] Detach: Pipe closed.\n");

    if (g_hUnhookDoneEvent) {
        CloseHandle(g_hUnhookDoneEvent);
        g_hUnhookDoneEvent = NULL;
    }

    OutputDebugStringA("[DLL] Detach: FreeLibraryAndExitThread now.\n");
    FreeLibraryAndExitThread(g_hModule, 0);
    return 0;
}

DWORD WINAPI InitThread(LPVOID lpParam) {
    WCHAR dllPath[MAX_PATH];
    if (GetModuleFileNameW(g_hModule, dllPath, MAX_PATH)) {
        LoadLibraryW(dllPath);
    }

    g_hUnhookDoneEvent = CreateEventW(NULL, TRUE, FALSE, NULL);

    g_hPipe = CreateFileW(L"\\\\.\\pipe\\nextvalley_tray_monitor", GENERIC_WRITE, 0, NULL, OPEN_EXISTING,
                          FILE_FLAG_OVERLAPPED, NULL);
    if (g_hPipe != INVALID_HANDLE_VALUE) {
        DebugOutput("[DLL] Pipeline connected.\n");
        HWND hTray = FindRealSystray();
        if (hTray) {
            DWORD windowPid;
            GetWindowThreadProcessId(hTray, &windowPid);
            if (windowPid == GetCurrentProcessId()) {
                g_OldWndProc = (WNDPROC)SetWindowLongPtrW(hTray, GWLP_WNDPROC, (LONG_PTR)ManualSubclassProc);
                if (g_OldWndProc) {
                    DebugOutput("[DLL] Successfully subclassed Shell_TrayWnd\n");
                }
            }
        }
    }
    CreateThread(NULL, 0, WatchdogThread, NULL, 0, NULL);
    return 0;
}

extern "C" __declspec(dllexport)
LRESULT CALLBACK GetMsgProc(int nCode, WPARAM wParam, LPARAM lParam) {
    return CallNextHookEx(NULL, nCode, wParam, lParam);
}

static bool IsExplorer() {
    WCHAR path[MAX_PATH];
    if (!GetModuleFileNameW(NULL, path, MAX_PATH)) return false;
    WCHAR *base = wcsrchr(path, L'\\');
    return base && _wcsicmp(base + 1, L"explorer.exe") == 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        if (!IsExplorer()) return TRUE;
        InitializeCriticalSection(&g_PipeCS);
        DisableThreadLibraryCalls(hModule);
        g_hModule = hModule;
        CreateThread(NULL, 0, InitThread, NULL, 0, NULL);
    } else if (ul_reason_for_call == DLL_PROCESS_DETACH) {
        if (g_hModule)
            DeleteCriticalSection(&g_PipeCS);
    }
    return TRUE;
}
