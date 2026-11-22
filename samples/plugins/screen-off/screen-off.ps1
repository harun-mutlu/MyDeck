Add-Type -Namespace MyDeck -Name Screen -MemberDefinition '[DllImport("user32.dll")] public static extern int PostMessage(int hWnd, int msg, int wParam, int lParam);'
[MyDeck.Screen]::PostMessage(0xFFFF, 0x0112, 0xF170, 2) | Out-Null
