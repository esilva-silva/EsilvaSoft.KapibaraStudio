# Revisão das licenças dos ZIPs Windows — 03/10/2026

## Resultado

Os dois ZIPs locais de v0.11.0 **não estão prontos para uma candidatura ao SignPath Foundation**. A MIT do KapibaraStudio não cobre todos os componentes distribuídos. Os pacotes incluem a CLI/runtime Copilot 1.0.85, arquivos OneAuth com termos Microsoft e Windows ML 2.1.1 com licença proprietária. Há também ausência de licenças e avisos no material que acompanha o download.

A revisão foi concluída sobre os arquivos identificados abaixo, incluindo os bundles single-file. Isso não constitui aprovação de redistribuição nem aceitação pelo SignPath. A correção dos pacotes e a decisão sobre a exceção de bibliotecas de sistema são ações posteriores; nenhum binário foi modificado, executado ou republicado nesta revisão.

## Artefatos e cobertura

Origem: `artifacts/release/0.11.0/`. Os hashes conferem com o `SHA256SUMS.txt` local.

| ZIP | SHA-256 | Arquivos externos | Entradas Desktop | Entradas MCP |
| --- | --- | ---: | ---: | ---: |
| `EsilvaSoft.KapibaraStudio-0.11.0-win-x64.zip` | `f7e0be5910b2000316329cf4f102d59d14e92f7064a25bd6a7298e2ebdef1e83` | 60 | 263 | 183 |
| `EsilvaSoft.KapibaraStudio-0.11.0-win-arm64.zip` | `de58bdbc77fbad35b8dc4830251931722703cf01c1158091a3601ad4fee143d9` | 60 | 265 | 183 |

São **1.014 registros**, incluindo os executáveis contêineres e suas entradas internas; essa contagem não significa 1.014 arquivos diferentes extraídos em disco. Diretórios ZIP vazios foram excluídos. Todas as entradas de terceiros foram relacionadas a arquivos do cache por SHA-256; artefatos `EsilvaSoft.KapibaraStudio.*` são identificados como código/configuração do projeto pela identidade e pelos manifests, sem afirmar reprodução bit a bit do build. Não restaram registros sem origem.

Os manifests internos identificam 58 identidades/versões NuGet, acrescidas dos dois runtime packs `.NET 10.0.12` self-contained. O inventário tem **60 identidades/versões**, das quais 59 têm arquivos correspondentes no payload; `Microsoft.Data.Sqlite/10.0.12` é um metapacote sem arquivo correspondente. Pacotes multissistema podem ter assemblies presentes sem incluir seus binários nativos Linux/macOS; a evidência de distribuição é a coluna de origem de cada arquivo, não apenas sua presença no grafo.

Evidências versionadas em [windows-license-review-2026-10-03/](windows-license-review-2026-10-03/):

- [files.csv](windows-license-review-2026-10-03/files.csv): cada arquivo, contêiner, tamanho, SHA-256, origem e escopo da licença declarada.
- [packages.csv](windows-license-review-2026-10-03/packages.csv): identidade/versão, declaração NuSpec, avisos disponíveis e correspondências no payload.
- [archives.json](windows-license-review-2026-10-03/archives.json), manifests `deps/` e `bundle-notices/`: identificação dos arquivos e runtime real.
- [inter-fonts.json](windows-license-review-2026-10-03/inter-fonts.json): licença e versão lidas nas tabelas das fontes incorporadas.
- [notices.csv](windows-license-review-2026-10-03/notices.csv) e `texts/`: cópias integrais das licenças/avisos encontradas no cache ou ZIP, deduplicadas por hash. `nuspecs/` preserva os manifests consultados.

## Licenças e componentes

| Conteúdo encontrado | Licença / observação | Consequência |
| --- | --- | --- |
| Desktop, assemblies do produto e proxy MCP | MIT do KapibaraStudio | Manter MIT e autoria; o proxy MCP não deve integrar o Release atual. |
| `.NET 10.0.12`, runtime self-contained e host nativo | MIT; avisos de terceiros dos runtime packs | O runtime vem com o aplicativo, não depende apenas de uma instalação externa. Levar os avisos dos dois packs ao download. |
| Avalonia 12.1.2, AvaloniaEdit 12.0.0, toolkit e várias bibliotecas gerenciadas | MIT segundo os NuSpecs fixados | Preservar licenças e atribuições. O CSV registra versões transitivas diferentes em Desktop/MCP. |
| `Avalonia.Fonts.Inter.dll` | Wrapper MIT; seis fontes Inter 3.019, commit `0a5106e0b`, sob **OFL-1.1** | A declaração MIT do pacote não substitui a licença das fontes. Incluir OFL e autoria; a evidência foi lida do binário correspondente ao ZIP. |
| `av_libglesv2.dll` / ANGLE | Texto BSD com três condições no pacote Avalonia.Angle.Windows.Natives | Preservar texto e autoria. |
| SkiaSharp 3.119.4 e HarfBuzzSharp 8.3.1.3, wrappers e DLLs nativas | MIT do wrapper e avisos próprios de terceiros | Avisos incluem Skia/ANGLE/BSD, HarfBuzz/Old MIT, Apache, FreeType e outros. Não resumir o binário inteiro a MIT. |
| MongoDB.Driver/Bson 3.11.1, DnsClient 1.6.1, ModelContextProtocol.Core 2.2.0 | Apache-2.0 declarada; MCP tem histórico de contribuições/avisos próprios | Incluir licença, atribuições e NOTICE aplicável. MongoDB Server/mongosh não foi encontrado nesses ZIPs. |
| Acornima 1.7.0, Snappier 1.3.1, Jint 4.16.0 | BSD-3-Clause, BSD-3-Clause e BSD-2-Clause, respectivamente | Incluir copyright, condições e disclaimer. |
| SQLitePCLRaw 2.1.12, `e_sqlite3.dll`, Microsoft.Data.Sqlite.Core 10.0.12 | Wrappers SQLitePCLRaw Apache-2.0, wrapper Microsoft MIT; SQLite upstream domínio público | Distinguir wrapper de engine e preservar os avisos dos pacotes. |
| ONNX Runtime Managed 1.28.0 e GenAI Managed/WinML 0.15.2, inclusive `onnxruntime-genai[-cuda].dll` | Licenças principais MIT; avisos nativos adicionais | Os avisos incluem **Intel Simplified Software License** e outros termos. Um aviso abrangente de pacote não prova que cada componente citado foi linkado ao binário Windows. Não declarar que MKL/CUDA/cuDNN estão ausentes nem presentes apenas por esse texto/nome. |
| **Microsoft.Windows.AI.MachineLearning 2.1.1**: `DirectML.dll`, `Microsoft.Windows.AI.MachineLearning.dll`, `onnxruntime.dll` | **Microsoft Software License Terms — Windows ML Runtime**, não MIT/OSI | Componente proprietário realmente identificado por hash nos dois Desktop. Revisar seção 3 de redistribuição, proteção contratual de usuários/distribuidores e avisos. Mesmo `onnxruntime.dll` neste pacote não deve ser automaticamente tratado como o binário de um pacote ONNX MIT diferente. |
| SDK .NET GitHub.Copilot.SDK 1.0.14 | MIT | Compatível com uma distribuição OSS com atribuição. Não confundir com a CLI/runtime. |
| **CLI/runtime Copilot 1.0.85**, scripts, schemas, definições, plugins, `runtime.node`, `copilot*.exe/dll` | **GitHub Copilot CLI License**, com restrições de modificação e redistribuição | Conteúdo proprietário da distribuição oficial, inclusive executáveis embutidos. A licença integral da CLI não acompanha esses ZIPs. MIT do SDK não concede licença para esses arquivos. |
| OneAuthInterop, MSAL runtime e plugin computer-use da distribuição Copilot | OneAuth tem **termos Microsoft próprios**; demais arquivos pertencem à distribuição CLI e podem ter termos adicionais | OneAuthInterop.LICENSE.txt é o único aviso externo encontrado, insuficiente para o conjunto. Termos independentes de MSAL/computer-use não foram individualizados pelo distribuidor no cache inspecionado. Isso é uma lacuna documentada, não uma suposição de MIT. |
| `rg.exe` e `tgrep.exe` vindos do pacote CLI | Origem exata confirmada por hash no cache CLI; avisos próprios não acompanham o ZIP | A licença geral da distribuição CLI não substitui os avisos upstream. Não atribuir a licença de ripgrep a tgrep. Remover o payload antigo na próxima reconstrução evita redistribuir esses helpers. |
| Pesos/tokenizers de modelos externos | Nenhum arquivo `.onnx`, `.safetensors` ou `.gguf` no inventário | Não aplicamos a MIT do produto a modelos instalados pelo usuário. |

O inventário por pacote complementa esta tabela e cobre também OpenAI, Anthropic, System.ClientModel, Microsoft.Extensions.*, SharpCompress, ZstdSharp.Port, Tmds.DBus.Protocol, MicroCom e bibliotecas auxiliares. Os textos primários suplementares devem acompanhar a revisão da licença declarada; `license_scope` é uma declaração de procedência, não certificação de que não existem subcomponentes com termos próprios.

## Lacunas concretas nos ZIPs

1. Ausência de `LICENSE` do produto, `THIRD-PARTY-NOTICES.md`, licenças principais e avisos nativos, inclusive OFL da Inter e termos Windows ML, tanto externamente quanto nos manifests dos bundles. Os termos OneAuth são a exceção encontrada, fora do bundle.
2. Presença de runtime Copilot 1.0.85, inclusive binários dentro do single-file. Verificar apenas diretórios externos é insuficiente para avaliar um pacote antigo.
3. Presença do proxy `mcp/EsilvaSoft.KapibaraStudio.McpServer.exe`, embora o Release vigente deva excluir a integração Claude Code.
4. `THIRD-PARTY-NOTICES.md` do checkout descreve ONNX principal como MIT, mas ainda precisa distinguir Windows ML proprietário, fontes OFL e avisos transitivos. A nova nota adicionada aponta estas evidências; ela não preenche retroativamente os ZIPs.

O código atual já usa `CopilotSkipCliDownload=true`, bloqueia a redistribuição de binários Copilot conhecidos e desativa o proxy MCP em Release. **Essas correções no checkout não alteram os ZIPs locais inspecionados.** A presente revisão não valida um pacote futuro nem o release atualmente hospedado no GitHub.

## SignPath e próximas ações

A [política da fundação](https://signpath.org/terms.html) exige componentes OSS e permite uma exceção para bibliotecas de sistema. Redistribuição permitida por uma licença proprietária não significa elegibilidade automática. A classificação de Windows ML/DirectML como exceção deve ser decidida pelo SignPath; não a assumimos. O Copilot redistribuído constitui outro obstáculo independente.

- Reconstruir ambos os ZIPs com o checkout atual e confirmar ausência completa de CLI/runtime Copilot e MCP, inclusive nas entradas internas.
- Gerar e empacotar licenças/avisos integrais por RID, incluindo MIT do produto, bibliotecas transitivas, fontes e componentes nativos.
- Resolver Windows ML: obter resposta sobre a exceção de bibliotecas de sistema ou planejar uma variante de distribuição com dependências OSS. Não trocar o backend de inferência como efeito colateral desta revisão.
- Para qualquer payload Copilot que se pretenda manter, obter termos e avisos upstream específicos; a análise presente não concede aprovação desses componentes opacos.
- Auditar os novos hashes antes de candidatar. SBOM formal por RID, análise jurídica dos termos proprietários e aprovação SignPath permanecem pendentes.

## Reprodução e limites

`eng/audit-windows-licenses.py` usa apenas a biblioteca padrão Python. Lê ZIP, header/manifest .NET e NuSpecs; compara bytes com o cache NuGet e o cache da CLI. Não inicia o app nem a CLI, não lê credenciais e não instala ferramentas. O parser segue o formato publicado em [Manifest.cs](https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs) e [FileEntry.cs](https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileEntry.cs). A execução aceita caminhos explícitos para caches e diretório de saída.

```powershell
python eng/audit-windows-licenses.py `
  --packages C:/Users/chuke/.nuget/packages `
  --source src/EsilvaSoft.KapibaraStudio.Infrastructure.Agents/obj/Release/net10.0/win-x64/copilot-cli/1.0.85/win32-x64 `
  --source src/EsilvaSoft.KapibaraStudio.Infrastructure.Agents/obj/Release/net10.0/win-arm64/copilot-cli/1.0.85/win32-arm64 `
  --output artifacts/license-audit-2026-10-03 `
  artifacts/release/0.11.0/EsilvaSoft.KapibaraStudio-0.11.0-win-x64.zip `
  artifacts/release/0.11.0/EsilvaSoft.KapibaraStudio-0.11.0-win-arm64.zip
```

Verificação: leitura dos quatro manifests de bundle, dois runtimeconfigs, 60 NuSpecs, comparação SHA-256, cobertura de todas as entradas, leitura das tabelas de seis fontes e inspeção de licenças/avisos suplementares. Termos primários consultados também no [pacote Windows ML](https://www.nuget.org/packages/Microsoft.Windows.AI.MachineLearning/2.1.1), [repositório Copilot CLI](https://github.com/github/copilot-cli/blob/main/LICENSE.md) e [Inter](https://github.com/rsms/inter/blob/master/LICENSE.txt); as versões locais fixadas e o nome/versionamento real dos binários prevalecem sobre páginas que possam mudar.

Esta é uma revisão de arquivos e declarações de licença, não uma engenharia reversa de todos os subcomponentes estaticamente linkados. Não certifica ausência de termos adicionais em binários proprietários, atendimento jurídico da seção de redistribuição nem elegibilidade definitiva ao SignPath. Não houve alteração de produção; build/testes .NET e testes visuais não foram executados para esta mudança documental e ferramenta manual.
