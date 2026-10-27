# Dependências Snapdragon e avisos de redistribuição

O build Windows ARM64 da IDE inclui ONNX Runtime GenAI WinML 0.18.0, Microsoft.ML.OnnxRuntime.Managed 1.31.0 e o plugin Qualcomm.ML.OnnxRuntime.QNN 2.7.0. O usuário autorizou a incorporação do QNN para a integração Snapdragon. A licença MIT do projeto permanece a licença do código da IDE; os componentes de terceiros mantêm seus próprios termos.

O pacote Qualcomm contém um `LICENSE` MIT para o plugin, avisos de terceiros e um `Qualcomm_LICENSE.pdf` separado para componentes do AI Stack. Este último estabelece licença proprietária, sem cobrança de royalties ou taxas, e permite redistribuir o software em código objeto incorporado à aplicação, não de forma independente; exige preservar os avisos. Não se deve descrever todo o payload QNN/QAIRT como MIT. A distribuição ARM64 inclui, em `licenses/`, `Qualcomm-QNN-LICENSE.txt`, `Qualcomm-AI-Stack-LICENSE.pdf` e `Qualcomm-QNN-ThirdPartyNotices.txt`, sem alterar seus bytes.

| Arquivo distribuído | Bytes | SHA-256 |
|---|---:|---|
| `Qualcomm-QNN-LICENSE.txt` | 1.140 | `f11e754f932564a0d8a9ad2c233bf671253dde137d3e2ef3082448bbe83e1764` |
| `Qualcomm-AI-Stack-LICENSE.pdf` | 147.577 | `ec1dccfdcba5c6e64126e84199b8362bf4999107bfa567ebe831dbb4c461692b` |
| `Qualcomm-QNN-ThirdPartyNotices.txt` | 69.810 | `d039f057d20690116daa1fc65165cd0221ee168208d770062a84c1b4d579abb1` |

O runtime WinML também possui licença própria da Microsoft. A presença dos arquivos de terceiros e a análise das condições de sua distribuição devem ser verificadas nos artefatos de release, conforme as políticas do projeto.

A prova de empacotamento local usou `dotnet restore ... --locked-mode -r win-arm64` e `dotnet publish ... --no-restore -r win-arm64`, com `SlopOnnxBackend=WinML`. A saída contém `onnxruntime_providers_qnn.dll`, `QnnHtp.dll`, `QnnSystem.dll`, stubs/skeletons e os três avisos. O payload nativo QNN ARM64 somou 281.728.145 bytes em 13 arquivos. Os binários do plugin e runtime foram inspecionados como PE ARM64; a geração e o uso efetivo da NPU não foram executados nesta máquina x64.

O pacote QNN está bloqueado no grafo WinML com seus assets automáticos excluídos. O Desktop copia o payload apenas para RID `win-arm64` ou build Windows nativo executado por processo MSBuild ARM64 sem RID; não deve copiá-lo para x64 ou Linux. Os lockfiles específicos `packages.WinML.win-arm64.lock.json` atendem à restauração com RID explícito. A avaliação MSBuild confirmou os ramos explícitos ARM64/x64 e Linux neste host; a compilação sem RID em host ARM64 permanece a validar no equipamento.

Referências oficiais: [pacote QNN 2.7.0](https://www.nuget.org/packages/Qualcomm.ML.OnnxRuntime.QNN/2.7.0), [notas QNN 2.7](https://github.com/onnxruntime/onnxruntime-qnn/blob/main/docs/release-notes.md) e [GenAI WinML 0.18.0](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntimeGenAI.WinML/0.18.0).
