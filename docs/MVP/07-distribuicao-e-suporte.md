# Distribuição e suporte

O MVP mantém desktop Windows/Linux e licença MIT, sem dependência comercial adicionada. Versões nas [fases](fases/README.md) são alvos de planejamento: não modificam AssemblyVersion, tags, releases, pacotes ou versão instalada.

## Modos de instalação e atualização

Windows e Linux portátil usam mecanismos de atualização aplicáveis à instalação, com origem do projeto, seleção por plataforma/arquitetura, verificação de integridade, falha visível e recuperação. Seleção/download aprovados isoladamente não comprovam atualização completa.

Linux gerenciado usa apt/dnf/pacman conforme pacote. O marcador válido de package-manager tem precedência sobre escrita, inclusive para root; o aplicativo não sobrescreve instalação gerenciada. Marcador inválido/ilegível leva a atualização manual. Tarball portátil continua separado.

Pacotes deb/rpm/AUR têm implementação e evidências de WSL/contêiner/CI; isso não significa publicação em repositório ou homologação em desktop nativo. Não orientar instalação via helper AUR enquanto o pacote do projeto não estiver publicado/verificado.

Dados locais devem permanecer preservados em instalar, atualizar, downgrade aplicável, remover e recuperar: workspace.db/workspace-log.db na área do usuário, com migrações compatíveis. Nenhum instalador deve recriar sessão vazia sobre dados ilegíveis.

## Dependências e artefatos

Auditar artefatos reais por RID, hashes, MIT/notices, transitivas/nativos e SBOM oficial. Copilot é instalado pelo usuário; não redistribuir CLI/runtime por resíduo de staging/bin/obj. Separar auditoria técnica do pacote das decisões de termos/licenças aplicáveis.

IA local exige pacote compatível e íntegro. CPU é referência inicial; GPU/NPU têm suporte condicionado por pacote/hardware e evidência. Instalação do Studio não exige modelo nem download silencioso de pesos.

## Matriz de suporte a fechar

Antes de declarar suporte estável, registrar Windows e distribuições Linux efetivamente exercitados, arquitetura, modo de instalação, ambiente gráfico, versões MongoDB/autenticação/topologia, runtime Copilot e pacote/modelo CPU. Ausência de evidência é pendência nominal; Windows não homologa Linux e emulação não homologa hardware nativo.

A Fase 4 demonstra atualizar/reabrir e recuperar; Fase 5 consolida ambientes reais; Fase 6 só libera alegações comprovadas. Não prometer rollback de escrita MongoDB ao cancelar nem backup operacional pela transferência lógica.

Os contratos de instalação, integridade, publicação e recuperação estão definidos neste documento; os resultados datados e limitações estão na [baseline](09-baseline-de-validacao.md).

## RIDs, backends e formatos

| RID | Backend padrão da matriz de release | Formato |
| --- | --- | --- |
| win-x64 | WinML | ZIP |
| win-arm64 | WinML | ZIP |
| linux-x64 | Cpu | tar.gz; deb/rpm e fonte de pacote AUR quando publicados |
| linux-arm64 | Cpu | tar.gz; deb/rpm e fonte de pacote AUR quando publicados |

SlopOnnxBackend aceita Cpu, WinML ou Cuda. WinML é exclusivo Windows; Cpu é referência do MVP e fallback padrão fora de Windows; Cuda é opção técnica de build condicionada a dependências/hardware e não é homologação ou distribuição universal. WinML não comprova GPU/NPU em qualquer equipamento.

Para build técnico CPU, passe `-p:SlopOnnxBackend=Cpu` consistentemente no restore/build/publish e use o lock correspondente; para Windows WinML, `-p:SlopOnnxBackend=WinML`; Cuda usa `-p:SlopOnnxBackend=Cuda`. Fixe RID por `-r win-x64`, `win-arm64`, `linux-x64` ou `linux-arm64` na preparação/publish correspondente, sem misturar assets restaurados de outro backend. Release usa PublishSingleFile=true e IncludeNativeLibrariesForSelfExtract=true. Esses flags descrevem composição, sem instruir execução automática de benchmark/modelo.

Fontes verificáveis: [propriedades](../../Directory.Build.props), [adapter/backends](../../src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter.csproj), [matriz release](../../.github/workflows/release.yml). Modelo/pacote precisa ser compatível com backend efetivamente distribuído; a Fase 2 deve declarar ambos no ensaio CPU.


## Linux gerenciado: layout e comandos

| Formato | Arquivo | Arquitetura |
| --- | --- | --- |
| deb | `kapibarastudio_<semver>_<amd64\|arm64>.deb` | Debian/Ubuntu |
| rpm | `kapibarastudio-<semver>-1.<x86_64\|aarch64>.rpm` | Fedora |
| fonte AUR | `kapibarastudio-bin-<semver>-aur.tar.gz` | Arch x86_64/aarch64, quando publicada |

Os pacotes reaproveitam o tarball intacto, instalam em `/usr/lib/kapibarastudio/`, criam `/usr/bin/kapibarastudio` como symlink e instalam desktop entry/ícones/licença. `package-manager` ao lado do executável contém `apt`, `dnf` ou `pacman`. Ler antes de verificar escrita, inclusive como root; marcador inválido/ilegível/diretório ou maior que 64 caracteres resulta em ManualOnly. Windows ignora o marcador.

Em PackageManaged, consultar versão não autoriza baixar/aplicar staging: não criar `updates/pending.json`, aplicar no encerramento ou limpar instalação gerenciada. A UI oferece copiar comando, abrir release e fechar. `SLOPSTUDIO_DISABLE_UPDATES=1` desativa atualização. O tarball portátil conserva seu caminho de atualização separado.

| Gerenciador | Instalar/atualizar arquivo já baixado | Downgrade | Remover |
| --- | --- | --- | --- |
| apt | `sudo apt install ./kapibarastudio_<semver>_amd64.deb` | `sudo apt install --allow-downgrades ./<antigo>.deb` | `sudo apt remove kapibarastudio` |
| dnf | `sudo dnf install ./<pacote>.rpm`; atualizar com `sudo dnf upgrade ./<pacote>.rpm` | `sudo dnf downgrade ./<antigo>.rpm` | `sudo dnf remove kapibarastudio` |
| pacman | Extrair fonte do pacote publicado; dentro da pasta `kapibarastudio-bin`, executar `makepkg -si` | `sudo pacman -U <pacote-antigo>` | `sudo pacman -R kapibarastudio-bin` |

Substituir nomes/arquitetura pelo asset realmente disponível. Não há repositório APT/DNF assumido; `dnf history undo` depende de encontrar a versão anterior. Não recomendar helper AUR antes da publicação/verificação do nome do projeto. O campo Version de deb/rpm usa `~` para pré-release; o nome do arquivo mantém SemVer original, sem `~`. Metadados `+build` são recusados pelo empacotamento. AUR não aplica strip ao bundle single-file.

Dependências incluem libc/C++, ICU, OpenSSL, zlib, fontes/fontconfig, ícones e X11. Pelo menos uma fonte é obrigatória; o aplicativo pode abortar sem ela. A matriz usa X11/XWayland; Wayland nativo não está homologado. Geradores e verificações são [LinuxDistroPackaging.ps1](../../eng/LinuxDistroPackaging.ps1), [nfpm.yaml](../../packaging/linux/nfpm.yaml) e [Test-LinuxDistroPackages.sh](../../eng/Test-LinuxDistroPackages.sh).

Dados em `$XDG_DATA_HOME/EsilvaSoft/KapibaraStudio` (`workspace.db`, `workspace-log.db`) não são removidos por instalação, upgrade, downgrade ou purge. Downgrade do pacote não garante compatibilidade de dados: testar migração/leitura sem apagar sessão ilegível. Validar hash dos dados antes/depois do ciclo e `dpkg --verify`, `rpm -V` ou `pacman -Qkk`; sob Xvfb, processo vivo não prova interação nativa. Arquivos de extração `~/.net` podem permanecer após remoção.

## Integridade, licenças e publicação

Auditar o arquivo final por RID, inclusive entradas do single-file: LICENSE MIT do produto, THIRD-PARTY-NOTICES, atribuições/fontes e avisos nativos. SDK Copilot MIT não autoriza redistribuir CLI/runtime. `CopilotSkipCliDownload=true` e guardas de build não substituem inspecionar staging limpo e payload final.

Windows ML 2.1.1 (`DirectML.dll`, `Microsoft.Windows.AI.MachineLearning.dll`, `onnxruntime.dll` desse pacote) tem Microsoft Software License Terms próprios; não presumir MIT por ser ONNX. Inter incorporada tem OFL-1.1 apesar do wrapper MIT. .NET, Skia/HarfBuzz, ONNX e transitivas exigem seus avisos. Pesos/tokenizers externos não fazem parte da publicação; MIT do Studio não os relicencia. O inventário de dependências e cópias de licenças disponíveis estão em [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md).

A revisão local de 03/10/2026 encontrou dois ZIPs Windows antigos v0.11.0 com runtime Copilot 1.0.85, proxy MCP e ausência de LICENSE/notices completos. Isso não descreve o pacote atual: reconstruir/auditar antes de distribuir. A revisão não concede aprovação jurídica, autorização de termos proprietários ou elegibilidade SignPath. Hash SHA-256 detecta corrupção, não comprometimento da conta/pipeline; não assumir assinatura GPG ou repositório assinado.

`workflow_dispatch` gera artefatos de teste sem publicar; tag/release e publicação AUR são ações separadas. Pacotes de upgrade de teste não são assets de produto. Registrar hashes, SBOM, RID, commit e decisão de termos para a distribuição real. Falha de download/integridade interrompe aplicação; recuperação não sobrescreve dados do usuário nem promete rollback MongoDB.
