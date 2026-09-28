# Backlog — cofre criptográfico de ambientes

**Origem:** CON-07 (ambientes), recorte não concluído. **Situação:** adiado, sem fase atribuída.

## O que existe hoje e permanece funcionando

O armazenamento local de ambientes é **necessário ao funcionamento atual** e foi preservado integralmente:

- `EsilvaSoft.KapibaraStudio.Core` — `EnvironmentVault`, `EnvironmentDefinition`, `EnvironmentSnapshot`.
- `EsilvaSoft.KapibaraStudio.Application` — `IEnvironmentVaultRepository` e a fachada em `WorkspaceService`.
- `EsilvaSoft.KapibaraStudio.Infrastructure` — `LiteDbConnectionProfileRepository.EnvironmentVault` e `OperationEnvironment`, que resolve `${ENV.get("...")}` nas URIs antes do primeiro `await`.
- `EsilvaSoft.KapibaraStudio.Desktop` — `EnvironmentsWindow` e `EnvironmentsViewModel`, preservados **sem ponto de entrada na interface**.

A separação de camadas já está correta; nenhum código precisou mudar de projeto.

## Três conceitos distintos — não confundir

| Conceito | Estado real |
| --- | --- |
| **Ambiente local** | Implementado. Conjunto nomeado de pares chave/valor (Development, Staging, Production ou personalizado), com ambiente ativo e invalidação de conexões ao trocar. |
| **Armazenamento de configuração** | Implementado. Os valores são gravados como **JSON em texto puro** na coleção `environmentVault` do LiteDB do workspace. O arquivo LiteDB é aberto **sem senha**. A UI mascara o valor na exibição; isso é mascaramento visual, não proteção do dado em repouso. |
| **Cofre criptográfico** | **Não implementado.** Não existe chave, derivação, cifra, rotação ou proteção contra leitura do arquivo por quem tem acesso ao disco. |

Senhas de conexão propriamente ditas não usam esse caminho: `SessionConnectionSecretStore` as mantém apenas em memória de sessão e nunca as grava em disco.

## Decisão

O botão **Ambientes**, rotulado "Ambientes / Key Vault", foi removido da barra superior. O rótulo prometia um cofre que não existe, e o requisito não pertence à fase atual.

Nenhum documento, mensagem de interface ou README pode descrever esse armazenamento como cofre seguro enquanto a cifra não for implementada **e** homologada.

## Escopo futuro, quando houver fase

- Cifra em repouso dos valores de ambiente, com decisão explícita sobre origem da chave.
- Política de rotação e de migração dos valores já gravados em texto puro.
- Comportamento definido para cofre ilegível — hoje `LoadEnvironments` já protege contra sobrescrever um cofre que não pôde ser lido, e essa garantia deve ser preservada.
- Reativação do ponto de entrada na interface, com rótulo correspondente ao que estiver de fato implementado.

## Documentos relacionados

[07 — Segurança e administração](../07-dados-seguranca-e-administracao.md) · [16 — Checklist de homologação](../16-checklist-homologacao.md) · [10 — ADRs](../10-decisoes-arquiteturais.md)

## Relação com a v0.11.0 — planejada em 22/09/2026

O [plano MCP/agentes](../phases/phase-07-v0.11.0/README.md) atribui à v0.11.0 um recorte específico de `ISecretStore` nativo para API keys/tokens de providers e credenciais MongoDB persistidas usadas pelos agentes, com apenas referências no LiteDB e modo de sessão quando o cofre não estiver disponível. A migração controlada dessas credenciais é gate antes da exposição por agentes. Isso não implementa nem conclui este backlog: gestão geral dos valores de ambiente, rotação e reativação da janela continuam preservadas acima, sem fase comprometida. Não migrar silenciosamente segredos MongoDB nem descrever o armazenamento existente como seguro.
