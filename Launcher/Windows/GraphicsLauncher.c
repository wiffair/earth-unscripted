/* Earth Unscripted graphics chooser. Original code: MIT. */
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <shellapi.h>
#include <xinput.h>
#include <stdio.h>
#include <wchar.h>
#include <string.h>

static const char *source="57d9e051356e0bbb3aa6b2b705b35cc5df7de768dd3ad9636fa6b06d8ab742f4";
static const char *version="0.24.0-beta.10";
static const wchar_t *names[]={L"Earth Unscripted Beta",L"Earth Unscripted Beta HD"};
static wchar_t root[32768];
static HWND window, buttons[2];
static HFONT heading, normal;
static HBRUSH background;
static int selected, uiSmoke;
typedef DWORD (WINAPI *GetPad)(DWORD,XINPUT_STATE*);
static GetPad getPad;
static WORD lastPad;

static void error(const wchar_t *message) {
    MessageBoxW(window,message,L"Earth Unscripted",MB_OK|MB_ICONERROR);
}
static int path(wchar_t *out,size_t capacity,int choice,const wchar_t *suffix) {
    return swprintf(out,capacity,L"%ls\\Players\\%ls\\%ls%ls",root,
        choice?L"HD":L"Standard",names[choice],suffix)>0;
}
static int valid(int choice) {
    wchar_t executable[32768], manifest[32768];
    if(!path(executable,32768,choice,L".exe")||GetFileAttributesW(executable)==INVALID_FILE_ATTRIBUTES) return 0;
    path(manifest,32768,choice,L"_Data\\StreamingAssets\\SharedGameSource.json");
    FILE *file=_wfopen(manifest,L"rb");
    if(!file) return 0;
    char data[4096];size_t length=fread(data,1,sizeof(data)-1,file);int trailing=fgetc(file);fclose(file);data[length]=0;
    return trailing==EOF && strstr(data,source)!=NULL && strstr(data,version)!=NULL;
}
static int running(void) {
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    if(snapshot==INVALID_HANDLE_VALUE) return -1;
    PROCESSENTRY32W process={0};process.dwSize=sizeof(process);int found=0;
    if(Process32FirstW(snapshot,&process)) do {
        if(!_wcsicmp(process.szExeFile,L"Earth Unscripted Beta.exe") ||
           !_wcsicmp(process.szExeFile,L"Earth Unscripted Beta HD.exe")) {found=1;break;}
    } while(Process32NextW(snapshot,&process));
    CloseHandle(snapshot);return found;
}
static int launch(int choice) {
    if(!valid(choice)){error(L"This graphics version is missing or out of date. Extract the complete matching game ZIP and try again.");return 0;}
    if(running()!=0){error(L"Please save and close the current Earth Unscripted drive before opening another graphics version.");return 0;}
    wchar_t executable[32768],directory[32768],command[32768];path(executable,32768,choice,L".exe");
    wcscpy(directory,executable);*wcsrchr(directory,L'\\')=0;
    swprintf(command,32768,L"\"%ls\"",executable);
    STARTUPINFOW startup={0};startup.cb=sizeof(startup);PROCESS_INFORMATION child={0};
    if(!CreateProcessW(executable,command,NULL,NULL,FALSE,0,NULL,directory,&startup,&child)){
        error(L"The game could not start. Keep the launcher, Players folders and all extracted files together.");return 0;
    }
    CloseHandle(child.hThread);CloseHandle(child.hProcess);return 1;
}
static void focus(int index){selected=index;SetFocus(buttons[index]);}
static LRESULT CALLBACK events(HWND hwnd,UINT event,WPARAM w,LPARAM l) {
    switch(event){
    case WM_COMMAND:
        if(LOWORD(w)==101||LOWORD(w)==102){if(launch(LOWORD(w)-101))DestroyWindow(hwnd);return 0;}
        if(LOWORD(w)==IDCANCEL){DestroyWindow(hwnd);return 0;}break;
    case WM_TIMER:
        if(w==2){DestroyWindow(hwnd);return 0;}
        if(getPad){XINPUT_STATE state={0};if(getPad(0,&state)==ERROR_SUCCESS){
            WORD pressed=state.Gamepad.wButtons & ~lastPad;lastPad=state.Gamepad.wButtons;
            if(pressed&(XINPUT_GAMEPAD_DPAD_LEFT|XINPUT_GAMEPAD_DPAD_UP))focus(0);
            if(pressed&(XINPUT_GAMEPAD_DPAD_RIGHT|XINPUT_GAMEPAD_DPAD_DOWN))focus(1);
            if(pressed&XINPUT_GAMEPAD_A)SendMessageW(hwnd,WM_COMMAND,101+selected,0);
            if(pressed&XINPUT_GAMEPAD_B)DestroyWindow(hwnd);
        }else lastPad=0;}return 0;
    case WM_CTLCOLORSTATIC:
        SetTextColor((HDC)w,RGB(245,240,222));SetBkColor((HDC)w,RGB(6,14,21));return (LRESULT)background;
    case WM_DESTROY:PostQuitMessage(0);return 0;
    }
    return DefWindowProcW(hwnd,event,w,l);
}
static HWND label(const wchar_t *text,int x,int y,int width,int height,HFONT font){
    HWND control=CreateWindowW(L"STATIC",text,WS_CHILD|WS_VISIBLE,x,y,width,height,window,NULL,NULL,NULL);
    SendMessageW(control,WM_SETFONT,(WPARAM)font,TRUE);return control;
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE previous,LPWSTR command,int show){
    (void)previous;(void)command;
    if(!GetModuleFileNameW(NULL,root,32768)||!wcsrchr(root,L'\\'))return 10;
    *wcsrchr(root,L'\\')=0;
    int count;wchar_t **args=CommandLineToArgvW(GetCommandLineW(),&count);
    for(int i=1;i<count;i++){
        if(!wcscmp(args[i],L"--check")){int result=valid(0)&&valid(1)?0:11;LocalFree(args);return result;}
        if(!wcscmp(args[i],L"--ui-smoke"))uiSmoke=1;
    }LocalFree(args);
    HANDLE mutex=CreateMutexW(NULL,TRUE,L"Local\\EarthUnscriptedBetaGraphicsChooser");
    if(!mutex||GetLastError()==ERROR_ALREADY_EXISTS){if(mutex)CloseHandle(mutex);return 12;}
    SetProcessDPIAware();
    background=CreateSolidBrush(RGB(6,14,21));
    WNDCLASSW cls={0};cls.hInstance=instance;cls.lpszClassName=L"EarthUnscriptedGraphicsChooser";
    cls.lpfnWndProc=events;cls.hCursor=LoadCursor(NULL,IDC_ARROW);cls.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(1));cls.hbrBackground=background;
    RegisterClassW(&cls);
    window=CreateWindowW(cls.lpszClassName,L"Earth Unscripted · Choose graphics",WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,
        CW_USEDEFAULT,CW_USEDEFAULT,760,430,NULL,NULL,instance,NULL);
    if(!window)return 13;
    heading=CreateFontW(-32,0,0,0,FW_SEMIBOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");
    normal=CreateFontW(-17,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");
    label(L"Earth Unscripted",28,28,590,52,heading);
    label(L"Choose your graphics",28,94,600,28,normal);
    label(L"Both versions use the same cars, progress and saved locations.",28,126,690,30,normal);
    for(int i=0;i<2;i++){
        buttons[i]=CreateWindowW(L"BUTTON",i?L"HD":L"Standard",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_PUSHBUTTON,
            28+i*356,180,326,62,window,(HMENU)(INT_PTR)(101+i),instance,NULL);
        SendMessageW(buttons[i],WM_SETFONT,(WPARAM)heading,TRUE);
    }
    label(L"Everyday performance",30,254,320,30,normal);
    label(L"Richer lighting and reflections",386,254,330,30,normal);
    HWND cancel=CreateWindowW(L"BUTTON",L"Cancel",WS_CHILD|WS_VISIBLE|WS_TABSTOP,600,318,110,34,window,(HMENU)IDCANCEL,instance,NULL);
    SendMessageW(cancel,WM_SETFONT,(WPARAM)normal,TRUE);
    HMODULE xinput=LoadLibraryW(L"xinput1_4.dll");if(!xinput)xinput=LoadLibraryW(L"xinput9_1_0.dll");
    if(xinput)getPad=(GetPad)(void*)GetProcAddress(xinput,"XInputGetState");
    SetTimer(window,1,50,NULL);if(uiSmoke)SetTimer(window,2,1500,NULL);
    ShowWindow(window,show);UpdateWindow(window);focus(0);
    MSG message;while(GetMessageW(&message,NULL,0,0)>0){
        if(message.message==WM_KEYDOWN){
            if(message.wParam==VK_ESCAPE){DestroyWindow(window);continue;}
            if(message.wParam==VK_LEFT||message.wParam==VK_UP){focus(0);continue;}
            if(message.wParam==VK_RIGHT||message.wParam==VK_DOWN){focus(1);continue;}
            if(message.wParam==VK_RETURN){HWND current=GetFocus();SendMessageW(window,WM_COMMAND,current==cancel?IDCANCEL:(current==buttons[1]?102:101),0);continue;}
        }
        if(!IsDialogMessageW(window,&message)){TranslateMessage(&message);DispatchMessageW(&message);}
    }
    if(xinput)FreeLibrary(xinput);DeleteObject(heading);DeleteObject(normal);DeleteObject(background);
    ReleaseMutex(mutex);CloseHandle(mutex);return 0;
}
