using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Input;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// Caractere que o layout de teclado ativo produz para uma tecla física de pontuação, ignorando Ctrl/Alt. Com Ctrl
/// pressionado o Windows não traduz pontuação (ToUnicodeEx devolve zero caracteres), e o Avalonia entrega
/// <c>KeySymbol</c> nulo; recorrer ao símbolo QWERTY da posição física erra em layouts como ABNT2, onde a tecla na
/// posição do <c>/</c> americano produz <c>;</c> — e <c>Ctrl+;</c> nunca chegava ao comando da IA.
/// </summary>
public static class KeyboardLayoutSymbol
{
    // Scan codes (set 1) das teclas de pontuação; letras, dígitos e teclas nomeadas casam por identidade de tecla.
    private static readonly Dictionary<PhysicalKey, uint> ScanCodes = new()
    {
        [PhysicalKey.Backquote] = 0x29, [PhysicalKey.Minus] = 0x0C, [PhysicalKey.Equal] = 0x0D,
        [PhysicalKey.BracketLeft] = 0x1A, [PhysicalKey.BracketRight] = 0x1B, [PhysicalKey.Backslash] = 0x2B,
        [PhysicalKey.Semicolon] = 0x27, [PhysicalKey.Quote] = 0x28, [PhysicalKey.Comma] = 0x33,
        [PhysicalKey.Period] = 0x34, [PhysicalKey.Slash] = 0x35, [PhysicalKey.IntlBackslash] = 0x56,
        [PhysicalKey.IntlRo] = 0x73
    };

    /// <summary>
    /// Fonte do caractere; o layout é global do SO, então testes Headless substituem a fonte e a restauram.
    /// Nulo significa "layout não informou" e mantém o fallback físico existente.
    /// </summary>
    public static Func<PhysicalKey, bool, char?> Resolver { get; set; } = Platform;

    public static char? Unmodified(PhysicalKey key, bool shift) => Resolver(key, shift);

    /// <summary>Fonte padrão: Windows consulta o layout da thread de UI; nas demais plataformas não há tradução.</summary>
    public static char? Platform(PhysicalKey key, bool shift) => OperatingSystem.IsWindows() ? FromWindowsLayout(key, shift) : null;

    [SupportedOSPlatform("windows")]
    private static char? FromWindowsLayout(PhysicalKey key, bool shift)
    {
        if (!ScanCodes.TryGetValue(key, out var scan)) return null;
        var layout = GetKeyboardLayout(0);
        var virtualKey = MapVirtualKeyExW(scan, MapScanCodeToVirtualKeyEx, layout);
        if (virtualKey == 0) return null;
        var state = new byte[256];
        if (shift) state[VirtualKeyShift] = 0x80;
        var buffer = new char[4];
        // DoNotChangeKeyboardState: consultar não consome nem arma tecla morta (´ ~ ^ `) do usuário.
        var count = ToUnicodeEx(virtualKey, scan, state, buffer, buffer.Length, DoNotChangeKeyboardState, layout);
        return count == 1 && !char.IsControl(buffer[0]) ? buffer[0] : null;
    }

    private const uint MapScanCodeToVirtualKeyEx = 3;
    private const int VirtualKeyShift = 0x10;
    private const uint DoNotChangeKeyboardState = 0x4;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint MapVirtualKeyExW(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[] buffer, int bufferSize, uint flags, IntPtr layout);
}
