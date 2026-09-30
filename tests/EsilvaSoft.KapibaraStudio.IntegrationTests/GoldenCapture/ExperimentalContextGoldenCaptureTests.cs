using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application.AiContext.Experimental;
using EsilvaSoft.KapibaraStudio.UnitTests.AiContext.Experimental;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.GoldenCapture;

[TestFixture]
[Category("Integration")]
public sealed class ExperimentalContextGoldenCaptureTests
{
    /// <summary>
    /// Captura os quatro goldens a partir da implementação atual. Explícito e com inspeção manual obrigatória do
    /// diff: regerar para fazer <c>ExperimentalContextGoldenTests.ArtifactsMatchTheFrozenFormat</c> passar apaga exatamente a regressão que
    /// este arquivo existe para mostrar.
    /// </summary>
    [Test, Explicit("Captura os goldens experimentais; exige inspeção manual do diff antes do commit."), Category("GoldenCapture")]
    public void CaptureGolden()
    {
        foreach (var contract in ExperimentalContextContracts.All)
        {
            var builder = new StringBuilder("# Golden do formato experimental ")
                .Append(contract.ContractId)
                .Append(" (lote A34b). Formato inalcançável em produção; não regenerar para esconder regressão.\n");
            foreach (var @case in ExperimentalContextCorpus.Cases)
            {
                var request = contract.Build(@case.Resolve(), @case.Settings);
                builder.Append("== ").Append(@case.Id).Append(" ==\n")
                    .Append("context=").Append(Escape(request.Context)).Append('\n')
                    .Append("sha=").Append(Sha256(request.Context)).Append('\n')
                    .Append("prefix=").Append(Escape(request.Prefix)).Append('\n')
                    .Append("suffix=").Append(Escape(request.Suffix)).Append('\n')
                    .Append("dictionary=").Append(Escape(string.Join("|", request.Dictionary))).Append('\n');
            }

            File.WriteAllText(GoldenPath(contract.ContractId), builder.ToString(), new UTF8Encoding(false));
        }
    }

    private static string GoldenPath(string contractId)
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.KapibaraStudio.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "Raiz do repositório não encontrada.");
        return Path.Combine(root!.FullName, "tests", "EsilvaSoft.KapibaraStudio.UnitTests", "AiContext", "Experimental", "Golden", contractId + ".golden");
    }

    /// <summary>
    /// Escapa para uma única linha lógica: a quebra de linha real vira o marcador <c>\n</c> (o arquivo é
    /// <c>text eol=lf</c>, então um <c>\r</c> só pode vir do conteúdo e é escapado como tal).
    /// </summary>
    private static string Escape(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (var character in raw)
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                _ => char.IsSurrogate(character)
                    ? "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture)
                    : character.ToString(CultureInfo.InvariantCulture)
            });
        return builder.ToString();
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(value)));

}
