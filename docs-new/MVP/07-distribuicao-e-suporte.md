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

Fontes históricas: [pacotes Linux](../../docs/architecture/linux-packages.md), [updater](../../docs/architecture/explorer-updater-review.md), [release Copilot](../../docs/architecture/release-copilot.md).

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
