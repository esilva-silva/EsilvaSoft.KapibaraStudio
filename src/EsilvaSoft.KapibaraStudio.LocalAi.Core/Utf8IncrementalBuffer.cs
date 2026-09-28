using System.Text;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Acumula bytes de um fluxo UTF-8 e devolve texto apenas nas fronteiras de caractere completas.
/// </summary>
/// <remarks>
/// Tokenizadores byte-level (BPE) emitem bytes, não caracteres: um acento, um CJK ou um emoji pode nascer partido
/// entre dois tokens. Decodificar cada token isolado produziria <c>U+FFFD</c> no meio da prévia. Este buffer segura
/// os bytes de uma sequência ainda incompleta até o token que a fecha.
/// A regra de bytes inválidos reproduz a "maximal subpart" do <see cref="Encoding.UTF8"/>: um byte que não pode
/// iniciar nem continuar uma sequência é consumido sozinho e vira um <c>U+FFFD</c>, exatamente como na decodificação
/// em lote do mesmo fluxo — é isso que garante que a concatenação dos pedaços seja idêntica ao lote.
/// </remarks>
public sealed class Utf8IncrementalBuffer
{
    private byte[] _pending = new byte[16];
    private int _count;

    /// <summary>Bytes ainda retidos por pertencerem a uma sequência incompleta.</summary>
    public int PendingBytes => _count;

    /// <summary>Acrescenta bytes e devolve o texto que já pode ser exibido; vazio se tudo ficou pendente.</summary>
    public string Append(ReadOnlySpan<byte> bytes)
    {
        if (_count + bytes.Length > _pending.Length) Array.Resize(ref _pending, Math.Max(_count + bytes.Length, _pending.Length * 2));
        bytes.CopyTo(_pending.AsSpan(_count));
        _count += bytes.Length;
        var complete = CompleteLength(_pending.AsSpan(0, _count));
        if (complete == 0) return "";
        var text = Encoding.UTF8.GetString(_pending, 0, complete);
        _pending.AsSpan(complete, _count - complete).CopyTo(_pending);
        _count -= complete;
        return text;
    }

    /// <summary>Encerra o fluxo: devolve os bytes pendentes decodificados (com <c>U+FFFD</c>) e esvazia o buffer.</summary>
    public string Flush()
    {
        if (_count == 0) return "";
        var text = Encoding.UTF8.GetString(_pending, 0, _count);
        _count = 0;
        return text;
    }

    /// <summary>Comprimento do maior prefixo que só contém sequências UTF-8 terminadas (válidas ou inválidas já decididas).</summary>
    private static int CompleteLength(ReadOnlySpan<byte> bytes)
    {
        var index = 0;
        while (index < bytes.Length)
        {
            var lead = bytes[index];
            if (lead < 0x80) { index++; continue; }
            var length = lead switch { >= 0xC2 and <= 0xDF => 2, >= 0xE0 and <= 0xEF => 3, >= 0xF0 and <= 0xF4 => 4, _ => 1 };
            // 0x80..0xC1 e 0xF5..0xFF nunca iniciam sequência: byte inválido isolado, decidido agora.
            if (length == 1) { index++; continue; }
            var consumed = 1;
            while (consumed < length)
            {
                if (index + consumed == bytes.Length) return index; // Sequência ainda pode se completar: retém.
                if (!IsContinuation(lead, consumed, bytes[index + consumed])) break;
                consumed++;
            }
            index += consumed; // Sequência completa, ou subparte máxima inválida já encerrada pelo byte seguinte.
        }
        return index;
    }

    /// <summary>Faixas do segundo byte que o UTF-8 restringe além de 0x80..0xBF, para excluir sobrecarga e substitutos.</summary>
    private static bool IsContinuation(byte lead, int position, byte value) => position == 1
        ? lead switch { 0xE0 => value is >= 0xA0 and <= 0xBF, 0xED => value is >= 0x80 and <= 0x9F,
            0xF0 => value is >= 0x90 and <= 0xBF, 0xF4 => value is >= 0x80 and <= 0x8F, _ => value is >= 0x80 and <= 0xBF }
        : value is >= 0x80 and <= 0xBF;
}
