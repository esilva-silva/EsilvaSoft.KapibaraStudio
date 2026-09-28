# Validação da meta de planejamento

## Polimento do Agente IA — evidência da integração CLP, 28/09/2026

**Estado:** os contratos, a persistência, as tools, o adapter Claude Code e a disponibilidade estão no HEAD `30b81a9`; painel global, Permissões, contexto em chips, histórico, confirmações e revisão de propostas estão no worktree compartilhado. A [ADR-056](../../10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026), o [plano e gates GCL-9..GCL-17](23-integracao-claude.md#polimento-do-agente-ia--adr-056-26092026), a [matriz](../../15-matriz-de-validacao.md) e o [roteiro C-37..C-45](21-homologacao-manual-login.md#casos-do-modo-claude-code-assinatura) foram atualizados. Presença de código e documentação não equivalem à aprovação de gate ou AC.

| Evidência da rodada CLP | Estado e limite |
| --- | --- |
| Restauração oficial | `dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode`: **exit 0** nesta rodada |
| Build oficial | `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=`: **exit 0, 0 avisos, 0 erros**. A propriedade evita a tarefa externa de telemetria Avalonia neste ambiente, sem desativar analisadores |
| Suíte oficial | `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore`: **exit 1**. UnitTests **3.782 aprovados, 22 ignorados, 0 falhas**; Benchmarks **43 aprovados**; Infrastructure.Agents.Tests **244 aprovados, 2 falhas em 246 executados**. Total: **4.069 aprovados, 2 falhas, 22 ignorados (4.093 casos)**. A suíte **não passou** |
| Falhas da suíte | `KillingTheTreeByParentLinksDoesNotReachAnOrphanedGrandchild` e `KillingOnlyTheCliProcessLeavesAnOrphanThatTheTreeCheckDetects`: o CLI falso não registrou filho/grandchild em 30 s, inclusive numa repetição fora do sandbox. O comportamento de árvore de processos continua sem prova positiva; não declarar GCL-6/11/17 aprovado |
| Suítes focadas | Propostas/anchors/ordem de eventos **8/8**; opt-out/LiteDB/ordem de eventos **20/20**; testes de screenshots/foco **3/3**. Esses filtros exercitam riscos específicos e não anulam as duas falhas da suíte oficial |
| UI e evidência visual | O orquestrador inspecionou PNGs reais nos temas claro e escuro do painel, da tela Permissões e do diff, nos tamanhos **960 × 620, 660 × 760 e 900 × 680**; o filtro de screenshots/foco passou **3/3**. Isso é inspeção de renderização, sem homologar leitor de tela, IME, diálogos nativos, Linux ou os casos manuais C-37..C-45 |
| MongoDB real, leitura isolada (28/09/2026) | O orquestrador relatou uma tentativa somente leitura com a URI fornecida pelo usuário: a conexão abriu e `ListDatabaseNames` concluiu com retorno. Nomes e contagem não foram extraídos; URI, nomes e conteúdo não foram registrados. A observação prova apenas conectividade e permissão de listar bancos naquele caminho isolado; não passou pelo Agente IA, pelo Claude Code, pelo broker MCP nem pelas tools do aplicativo |
| Homologação C-41/GCL-17 | **Pendente.** O caso C-41 exige, no Windows, conta Claude própria e tools do aplicativo via MCP para conexões, bancos, collections, índices e schema em cache com MongoDB real, incluindo negações e ausência de credenciais na saída. A leitura isolada acima não substitui esse roteiro, a revisão de diff C-43, o reinício/retomada nem os demais casos C-37..C-45. Linux continua sem proof de canal para tools MCP |

Nenhum gate GCL-9..GCL-17 ou AC relacionado é declarado aprovado nesta atualização. O restore e o build passaram, mas a suíte oficial tem duas falhas; as contagens históricas nas seções seguintes permanecem restritas às respectivas rodadas.

## Prioridade de contas 7B/8B — 25/09/2026

Planejamento atualizado por pedido do usuário: conta Claude entra no 8B condicional e passa a prioridade junto de Codex/ChatGPT (7B). Requisitos, providers, autenticação, plano, testes, aceites, fontes, roteiro manual, matriz, catálogo, ADRs, guia, design system e memória sincronizados. Nenhum código de produto ou credencial alterado; nenhum login executado ou AC aprovado.

Validação desta rodada: índice offline regenerado com 115 documentos; comparação de conteúdo do snapshot com arquivos passou; 332 destinos de links locais nos 18 documentos/contratos da alteração conferidos, nenhum ausente (âncoras não validadas automaticamente); 16 adapters Claude conferidos por `node scripts/sync-claude-agents.cjs --check`; `git diff --check` sem erros. Revisão independente dos diffs de 01/04/07/10/12/15 e contrato agent-provider-agent não encontrou problemas concretos; roteiro 21 revisado sequencialmente pelo autor, sem alegar revisão independente desse arquivo.

Limitação: `node scripts/check-docs-reader.cjs` falhou com `MODULE_NOT_FOUND: playwright`; não houve smoke visual do leitor. Nenhuma asserção foi alterada. Build/test .NET não executados nesta mudança exclusivamente documental. Todos os gates funcionais 7B/8B e a homologação manual H-01..H-17 permanecem pendentes.

## Preparação dos agentes — 24/09/2026

Validação documental da [matriz 20](20-agentes-e-execucao.md) e dos contratos de desenvolvimento, sem implementar features nem aprovar ACs:

- `node scripts/sync-claude-agents.cjs` e `--check`: 16 adapters gerados/verificados a partir de 17 perfis canônicos (coordenador na sessão principal); estrutura obrigatória de 14 seções conferida.
- Checagem local de 62 documentos e 370 links relativos: nenhum destino ausente; matriz cobre lotes 0–12 e AC-01..20.
- `node scripts/build-docs-index.cjs`: snapshot offline atualizado com 114 documentos. `git diff --check` do escopo documental passou.
- Formato Claude conferido contra [subagents](https://code.claude.com/docs/en/sub-agents) e [memória](https://code.claude.com/docs/en/memory) oficiais. Executável `claude` ausente no PATH desta sessão: descoberta e execução real dos adapters **não homologadas**.
- Restore/build/testes .NET não reexecutados nesta alteração de instruções; resultados históricos abaixo continuam limitados aos respectivos incrementos. Nenhuma mudança de UI exige nova evidência visual neste recorte.

## Histórico de implementação e evidências

Data-base: **22/09/2026**; atualização de andamento: **23/09/2026**. A v0.11.0 está em desenvolvimento inicial: o lote 0 reúne spikes isolados; o lote 1 possui adapters de cofre Windows/Linux e composição singleton dos stores e de `IAgentCredentialProvider` no DI da aplicação; e o lote 2 iniciou contratos de política, avaliador e registry interno de `list_connections`. Não há migração de credenciais, broker/servidor MCP ou DI de broker, protocolo externo, runtime, chat, provider, integração de API Key ou ferramenta MongoDB exposta nesta fase; **AC-01 a AC-20 permanecem pendentes**.

## Evidência executada

| Verificação | Resultado e limite |
| --- | --- |
| Leitura do pedido anexado e confronto da solução | Nove projetos existentes analisados por responsabilidades, contratos e caminhos; gaps rastreados em [13](13-analise-do-codigo.md) |
| Documentação e roadmap | Inventário dos 91 Markdown externos à nova fase; mapa de preservação em [14](14-migracao-documental.md) |
| Preservação dos escopos | Comparação com `git show HEAD` dos três READMEs movidos: todas as linhas anteriores preservadas após renumeração; workflow conserva bloco original integral com nota técnica adicional |
| Restore | `dotnet restore EsilvaSoft.SlopStudio.slnx --locked-mode` aprovado; primeira tentativa isolada não leu NuGet.Config do usuário, repetição autorizada concluiu |
| Build | `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=` aprovado, 0 avisos/0 erros; propriedade evita tarefa externa de telemetria conforme AGENTS.md |
| Testes iniciais | `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore`: UnitTests 2.674 aprovados, 20 ignorados, 0 falhas; Benchmarks 43 aprovados, 0 falhas. Registro histórico da validação documental inicial; não valida integrações futuras. |
| Validação integrada final | Após os incrementos de composição singleton, UTF-16 inválido e deadline não cooperativo, `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=` concluiu com **0 avisos/0 erros**. `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore` concluiu com **2.841 UnitTests aprovados, 21 ignorados e 0 falhas**, além de **43 Benchmarks aprovados, 0 ignorados e 0 falhas**. O round-trip nativo Windows ignorado por `1312 / NoLogonSession` é evidência separada do cofre e não prova o Credential Manager; não somar esse limite ao resultado da suíte. |
| Revisão independente de contratos | Seis achados corrigidos: grants, tetos de payload, prazo de aprovação, credencial IPC do proxy, cursores futuros e vocabulário/links. Rechecagem documental aprovada |
| Fontes externas | Documentação oficial de providers/MCP/plataformas e licenças abertas; registros datados em [15](15-fontes-e-licencas.md); artefatos/transitivas finais dependem da versão futura selecionada |
| Índice e referências | 109 documentos no snapshot offline, todos comparados integralmente com seus Markdown e processados pelo parser existente; zero destinos locais ausentes em docs, README raiz e avisos de terceiros; referências antigas mantidas somente como histórico explícito |
| Integridade do diff | `git diff --check` aprovado; sem alterações em src/tests, projetos, solução ou lockfiles. Avisos de conversão LF/CRLF do Git não são avisos de compilação |

Não foram executados login, chamadas pagas, MongoDB real, instalação, Secret Service/Credential Manager, servidor MCP nem screenshots de uma UI futura. Os testes ignorados não são evidência positiva de modelo/hardware real. Não houve alteração visual de produto, logo não se reivindica nova homologação de PNGs, leitor de tela ou diálogos nativos. O smoke browser legado (`check-docs-reader.cjs`) não foi usado como prova: seu número fixo de 84 entradas é anterior ao inventário atual; a verificação desta meta compara snapshot/arquivos e links diretamente.

## Auditoria requisito por requisito do pedido

Os números abaixo correspondem às seções do pedido, não a funcionalidades já implementadas.

| Item | Entrega inspecionada | Situação da meta documental |
| --- | --- | --- |
| 1 — Roadmap/migração | 14, índices, catálogo, inventário e fases 7–10 | Preservado e renumerado, incluindo homologação preexistente |
| 2 — Três componentes desacoplados | 02, ADR-046/047/048 | Fronteiras e grafo definidos |
| 3 — Runtime e protocolo interno | 03 | Portas, DTOs, eventos, estados, correlação, filas e cancelamento definidos |
| 4 — OpenAI/Codex | 04/07/15 | App Server condicionado por suporte oficial/confinamento; API direta escolhida como baseline inicial, com contrato offline testado e gates de provider/auth documentados |
| 5 — Claude | 04/07/15 | SDK C# candidato compilável, Agent SDK avaliado, limitação de login explícita; sem provider homologado |
| 6 — Providers futuros | 03/04 | DI/capabilities/conformidade; Copilot futuro |
| 7 — IA local | 02/03/04/13 | Reuso ONNX e limites do chat FIM preservados |
| 8 — MCP Server | 05 | Proxy, broker, identidade, lifecycle e recovery |
| 9 — Registry único | 02/03/06/08, ADR-048 | Handler e política comuns, sem implementação duplicada |
| 10 — Tools existentes | 06/13 | Reuso por método, gaps, schemas/saídas e catálogo de liberação |
| 11 — Risco | 06/08 | READ_ONLY/WRITE/DESTRUCTIVE/ADMINISTRATIVE e bloqueios |
| 12 — Permissões | 06/08 | Grants canônicos, escopos, deny default e revalidação |
| 13 — Approvals | 03/08/16 | Proposta imutável, one-shot, validade, precondição e UI confiável |
| 14 — Protocolos distintos | 02/03/05 | MCP versus runtime versus adapter explicitados |
| 15 — Transporte/processo | 05, ADR-047 | STDIO inicial, HTTP opcional, duas eras, Windows/Linux e tradeoffs |
| 16 — Chat Avalonia | 16 | Painel nativo, tools, erros, foco e testes visuais futuros |
| 17 — Seletor/provider/auth | 04/07/16 | UI por capabilities e métodos oficiais reais |
| 18 — Capabilities | 03/04 | Interseção de adapter/modelo/política e extensão sem branches UI |
| 19 — Credenciais | 07, ADR-049 | Cofre por SO, referências, migração/recovery e ausência de fallback plaintext |
| 20 — Conexões protegidas | 06/07/09 | IDs lógicos, resolução local e DTO allowlist |
| 21 — Contexto | 09/16 | Escopos independentes, prévia e orçamento |
| 22 — Privacy boundary | 09, ADR-051 | Conectar não envia dados; tool/ação/consentimento explícitos |
| 23 — Auditoria | 08/09 | Identidade/decisão/desfecho sem conteúdo sensível; falhas visíveis |
| 24 — Arquitetura confrontada | 02/13 | Reuso camadas/proprietário, parser literal e gaps concretos |
| 25 — Projetos | 02/10 | Dois projetos novos propostos, sem fragmentação artificial |
| 26 — Licenças | 15 e THIRD-PARTY-NOTICES | Pesquisa por candidato, SDK versus serviço, gate de transitivas/artefato |
| 27 — Documentação própria | README e 01–17 | Índice navegável e documentos pt-BR |
| 28 — ADRs | ADR-046–051 | Seis decisões não triviais propostas com alternativas/consequências/gates |
| 29 — Etapas executáveis | 10 | Objetivo, dependências, projetos, interfaces/classes, testes e conclusão por lote |
| 30 — Primeiro MCP seguro | 01/05/06/10 | Read-only antes de writes, todos com autorização/auditoria |
| 31 — Testes | 11/12 | Matriz de contratos, falhas, integração real, SO e secrets sem chaves na CI |
| 32 — Degradação | 03/04/07/11/16 | IDE funcional sem providers/rede/modelo/MCP |
| 33 — Aceites | 12 | AC-01..20 com evidência, todos futuros e não marcados aprovados |
| 34 — Resultado da meta | Conjunto completo e relatório presente | Plano e migração entregues; implementação continua em fase futura |

## Referências históricas recuperadas

A varredura encontrou 17 links de código que ainda apontavam para caminhos anteriores à divisão de assemblies; foram direcionados aos arquivos existentes correspondentes. Quatorze links para TRX históricos não presentes no checkout foram convertidos em referências textuais com aviso de indisponibilidade, conservando nome e caminho. Não foram inventados relatórios para substituir esses artefatos. As afirmações históricas mantêm suas datas e não são usadas como prova da nova fase.

## Acompanhamento posterior — spike de disponibilidade/schema Codex

Em 22/09/2026, foi executado no Windows o spike local descrito em [`eng/spikes/phase-07/codex-app-server`](../../../eng/spikes/phase-07/codex-app-server/README.md). `codex.exe --version` reportou `codex-cli 0.155.0-alpha.16`; o SHA-256 antes e depois da geração foi `97d4d67419d0ac2f71342f9a5e850f9468aa622618de8ea823223edb9a91926a`. O comando `app-server generate-json-schema --out` gerou **310 schemas JSON (3.512.403 bytes)**. O manifesto registra `credentialsCaptured=false`; houve **zero divergências de hash** entre os 310 arquivos e os valores manifestados. Diretório de saída e manifesto somaram **3.559.959 bytes**. A evidência ficou em `%TEMP%\slop-phase07-codex-review-edc3773c90604717a023886faca3cc52`, fora do repositório e local a este host Windows, sem fixture versionada ou artefato portátil.

O resultado aprova **somente disponibilidade, versão e geração de schema para avançar internamente no lote 0**. Não aprova critério AC nem encerra lote ou fase. O App Server não foi iniciado nessa coleta; o handshake foi verificado posteriormente no probe separado abaixo. `CODEX_HOME` isolado e `credentialsCaptured=false` não provam backend `keyring` nem sandbox/confinamento de filesystem, processo, ferramentas nativas ou rede. A revisão dos caminhos não oferece proteção atômica contra TOCTOU. Também seguem pendentes autenticação, sessão/turno, compatibilidade, MCP dual-era, Anthropic, cofres por SO, licenças/transitivas e os demais gates. Não houve build/testes, alteração de dependências ou mudança funcional do produto nessa coleta; a v0.11.0 continua **planejada** e **AC-01..AC-20 permanecem pendentes**.

Em 23/09/2026, o coletor revisado foi repetido após validação da ACL Windows restrita ao usuário atual. Manifesto local em `%TEMP%\slop-phase07-codex-final-8d8528541ddb414ca64999967285304e`; mesma versão e hash, 310 JSON/3.512.403 bytes, com zero arquivo ausente, divergente ou JSON inválido na revisão independente. Foi usada somente a saída `--version` e geração de schema; sem auth, daemon, sessão, turno ou tool. A nova evidência continua local ao host e não muda o gate parcial.

## Acompanhamento posterior — handshake STDIO Codex

Em **22/09/2026, 23:59:50 -03:00** (`2026-09-23T02:59:50.8484672Z`), o probe revisado `Probe-CodexAppServer.ps1` executou somente `codex app-server --listen stdio://` no **Microsoft Windows 10.0.26200**, com **PowerShell 7.6.6**. O executável correspondeu ao SHA-256 `97d4d67419d0ac2f71342f9a5e850f9468aa622618de8ea823223edb9a91926a` antes/depois; a versão `codex-cli 0.155.0-alpha.16` vem do manifesto de schema com esse mesmo hash, sem uma segunda chamada `--version` pelo probe. Uma execução anterior está registrada no diretório temporário da sessão; esta revisão mais recente gerou relatório em `%TEMP%\slop-phase07-codex-handshake-evidence-d38c8c97a1df406d8ac9c228b90f96c8\codex-app-server-handshake.json`.

| Observação real | Resultado e limite |
| --- | --- |
| Inicialização | `initialize` ID 1 recebeu resposta válida de **332 bytes**, sem contar newline; campos obrigatórios conferidos, `codexHome` correspondeu ao scratch, plataforma retornada `windows/windows` |
| Finalização | `initialized` enviado (notificação sem ACK próprio), stdin fechado, EOF/saída normal com **exit 0** |
| Saída limitada | **203 bytes** posteriores em stdout descartados, **0 bytes** em stderr; conteúdo bruto não persistido nem interpretado como evidência de capacidades |
| Ambiente/scratch | cwd e perfis em diretório privado; ambiente reduzido; config do scratch solicitou `ephemeral`; **0 auth.json** encontrados somente nessa árvore e scratch removido |
| Limites do probe | 15 s no transporte, 16 KiB no frame de resposta, 64 KiB em cada stream drenado; não é uma medição de confinamento do processo |
| Testes sintéticos | Cinco cenários de transporte passaram; seis cenários do manifesto passaram e o cenário de symlink no arquivo foi `SKIP` por falta de privilégio para criá-lo no Windows. Incluíram resposta/EOF, ID incorreto, limites, timeout, metadados/JSON e junction ancestral; não executam Codex nem substituem Linux |

O relatório guarda somente metadados saneados e não é um artefato portátil versionado. Script, helper e cenários reproduzíveis estão no [spike](../../../eng/spikes/phase-07/codex-app-server/README.md#probe-separado-de-handshake-stdio).

Isso confirma **somente a resposta de inicialização STDIO e o encerramento observado nessa instalação Windows**. Não houve login/logout, thread/turno, prompt ou chamada de tool; não foi iniciado comando daemon pelo probe. Não foram inspecionados arquivos de autenticação do usuário nem o backend keyring. A configuração efêmera e a ausência de auth.json no scratch não provam isolamento de credenciais, inexistência de leitura de configuração gerenciada/global, nem ausência de atividade interna de rede/arquivos/processos. A documentação oficial atual ainda classifica o comando App Server como experimental e sem suporte para produção; portanto esse handshake não libera o produto para adotar essa integração. O lote 0 permanece aberto até comprovar confinamento, storage, provider OpenAI real e demais gates. **AC-01..AC-20 pendentes; nenhuma integração de produto entregue por esse handshake.**

## Acompanhamento posterior — SDK MCP dual-era

Em 22/09/2026, spike independente em `eng/spikes/phase-07/mcp-sdk` fixou `ModelContextProtocol` 2.2.0 e `net10.0`. Restore locked e build passaram sem warnings; a revisão de 23/09 executou **10 testes Windows**, quatro SDK↔SDK e seis com cliente BCL independente em processo separado. O cliente não referencia servidor, SDK MCP ou pacote NuGet e implementa JSON-RPC com `System.Text.Json` e streams BCL. Os pipes STDIO reais exercitam as eras `2025-11-25` e `2026-07-28`, descoberta/listagem sintética vazia e rejeição cruzada de versões incompatíveis. As fixtures são expectativas sintéticas do spike, não schemas ou suíte oficial completa. Foram registrados transcripts, locks e inventário reproduzível de pacotes, licenças, notices e hashes; nenhuma dependência entrou na solução do produto.

O cliente BCL reduz a dependência de interoperabilidade SDK↔SDK, mas a prova continua restrita à descoberta e listagem vazia contra este servidor sintético no Windows. Não cobre clientes comerciais (Claude/Codex), Linux, sessões simultâneas, broker, autorização, tools de dados, payload hostil nem conformidade integral; o inventário não é SBOM de distribuição. Portanto aprova apenas o candidato para seguir no lote 0.

Na mesma revisão, os quatro testes `MongoReal` da solução foram executados contra MongoDB 8.0.30: 4/4 passaram. O fixture agora limpa o dbpath temporário em sucesso e falha; os logs ficam fora do dbpath para permitir remoção após encerrar os processos. A verificação confirma os quatro cenários, sem ampliar a homologação da Fase 7.

## Acompanhamento dos Lotes 1–2 — cofres, política e registry internos

Foram adicionados em `src/EsilvaSoft.SlopStudio.Application` `ISecretStore` (disponibilidade, leitura, gravação e exclusão assíncronas) e `IAgentCredentialProvider`; `SecretReference` opaca/versionada fica em Core. Resultados carregam só sucesso ou categoria tipada (`Unavailable`, `Locked`, `Denied`, `Cancelled`, `NotFound`, `Corrupt`), sem mensagem de exceção do backend. Dispensa de prompt usa `Cancelled`; cancelamento da operação permanece pelo token do chamador. Testes `SecretStoreContractsTests`: **10/10 aprovados** em reexecução focada, com build dos projetos referenciados e `-p:UsedAvaloniaProducts=`.

`WindowsCredentialSecretStore` foi adicionado em Infrastructure como adapter do Credential Manager: leitura, gravação, remoção, disponibilidade sem escrita, limpeza de buffers e falha tipada para UTF-16 inválido. O round-trip nativo foi **ignorado** neste processo por `1312 / NoLogonSession`. `LinuxSecretServiceSecretStore` também foi implementado e possui testes focados. Ambos os stores e `IAgentCredentialProvider` são compostos como singletons no DI da aplicação. A revisão estática independente permite avançar, mas não transforma testes focados ou composição em homologação nativa: D-Bus/Secret Service Linux, round-trip Windows, reinício, bloqueio, isolamento por conta e recuperação após falha permanecem pendentes.

O lote 2 iniciou contratos de política/permissão, avaliador e registry interno com `list_connections`. Deadline não cooperativo é limitado por `Task.WaitAsync`, com observação de fault tardio para evitar perda da falha após o retorno por prazo. Não há protocolo MCP, DI de broker, schema público, controle completo de saída, auditoria durável ou qualquer tool exposta. A execução focada final passou com **142 aprovados e 1 ignorado**. Não foram adicionados UI, persistência de `SecretReference`, migração de perfis Mongo ou provider. Os ACs permanecem pendentes.

## Acompanhamento posterior — SDK Anthropic compilável

Em 22/09/2026, o spike isolado `eng/spikes/phase-07/anthropic-sdk` fixou `Anthropic` 12.50.0; tag `Anthropic-v12.50.0` e nuspec identificam o mesmo commit `2beeb9f9b402b1cdb8030e7cdc38cc6cae5d42aa`. Restore locked passou, build `net10.0` terminou com zero avisos/erros, assinatura NuGet foi verificada e a auditoria não reportou vulnerabilidades. O inventário reproduzível registra dois pacotes resolvidos, licenças, commits, assets e hashes; não é SBOM final por RID.

O contrato usa modelo/texto sintéticos e compila a API de streaming, mas não executa cliente nem enumera eventos. O spike declara `ApiKey` e `AuthToken` nulos antes do construtor porque o construtor padrão tenta resolver configuração/credencial. Um handler bloqueador protege uma invocação futura, porém não houve chamada HTTP, API key, conta, teste de streaming/tool calling real ou execução Linux. Isto aprova somente o candidato e sua superfície compilável para continuar o lote 0, sem aprovar integração/provider ou AC.

## Acompanhamento posterior — Credential Manager Windows

Em 23/09/2026, um probe isolado chamou a API nativa Credential Manager no Windows 10.0.26200.0, processo x64 e PowerShell 7.6.6. A evidência estruturada preservada registra três casos em que `CredWriteW` falhou com `1312 / NoLogonSession` antes de gravar. Uma execução interativa anterior foi reportada como aprovada, incluindo readback e limpeza, mas seu relatório foi sobrescrito e não há transcript versionado para revisão; portanto ela não serve como prova reproduzível neste checkout. O [README do spike](../../../eng/spikes/phase-07/windows-credential-manager/README.md) distingue o relato da evidência atual.

O probe não enumera credenciais, não lê/usa credenciais preexistentes, não grava segredo em texto e não acessa API/provider. A falha estruturada mostra indisponibilidade neste contexto sem sessão; os relatos interativos ainda precisam ser repetidos e preservados. Não testa negação por segunda conta, cofre bloqueado, reinício ou limpeza após encerramento forçado/queda do SO. Não é implementação `ISecretStore` nem prova do backend Linux. WSL não tem distribuição instalada/operacional e retornou `E_ACCESSDENIED`; Docker/Podman e runtime D-Bus tampouco estão disponíveis neste host, logo Linux segue sem execução nativa.

## Acompanhamento posterior — contrato OpenAI API direto

Em 23/09/2026, o spike isolado [`openai-api`](../../../eng/spikes/phase-07/openai-api/README.md) fixou `OpenAI` 2.14.0 (MIT), tag/commit, lockfiles e inventário de 24 dependências com licenças declaradas e hashes. Restore locked local e build passaram; **5/5 testes** passaram contra handler offline: SSE com UTF-8 fragmentado, coleta de chamadas de função, allowlist/schema fechados, continuidade de `tool_call_id`, rejeição de stream incompleto e cancelamento isolado. O token do fixture é sintético, retido no processo e verificado pelo handler; não houve chamada de rede nem credencial real.

A API Responses foi excluída do contrato testado porque seus tipos no SDK 2.14.0 emitem `OPENAI001`; Chat Completions compilou com warnings-as-errors sem supressão. A tentativa de auditoria de vulnerabilidades não concluiu: o feed NuGet falhou por TLS/credenciais, e o restore offline desativou apenas a consulta de audit. Portanto a baseline inicial passa a ser API OpenAI direta por contrato, não por provider real; contas, modelos, custos, rede, capabilities e auditoria de dependências seguem pendentes. Nenhum pacote foi adicionado ao produto; AC-01..AC-20 continuam pendentes.

## Binding e adapter Linux para Secret Service

A revisão identificou `Tmds.DBus.Protocol` 0.94.1 (MIT, tag `rel/0.94.1`, commit `b4a7fed0b878f74cb54f7cca84d2889af4e596ba`) como binding de baixo nível para um adapter próprio, preferível ao wrapper high-level sem cancelamento claro e com prompts implícitos. `LinuxSecretServiceSecretStore` e suas camadas de protocolo/criptografia foram implementados de modo isolado e passaram **84 testes focados em execução anterior**. O adapter está agora selecionado no DI conforme o SO, mas essa composição não demonstra operação nativa. Proveniência permanece no [spike de viabilidade](../../../eng/spikes/phase-07/linux-secret-service/README.md); o pacote consta transitivamente no lock Desktop via Avalonia. Isso não é prova operacional: WSL/runtime de containers/D-Bus não estão disponíveis neste host e nenhum serviço D-Bus foi acessado nesta continuação. Prompt dispensado, bloqueio concorrente, ausência de serviço e opt-in/headless seguem tratados como comportamento a validar em Linux nativo; por instrução do usuário, não foram executados testes Linux agora. Linux permanece sem homologação nativa.

## Revalidação da solução-base durante a continuação da meta

Em 23/09/2026, o restore oficial primeiro bloqueou ao tentar ler o `NuGet.Config` global protegido pelo sandbox. Repeti restore locked com `NuGet.Config` temporário sem fontes externas e o cache global local de pacotes; os oito projetos restauraram. Build da solução passou com `-p:UsedAvaloniaProducts=`, **0 avisos e 0 erros**. A primeira suíte encontrou três falhas Mongo porque centenas de diretórios de dados descartáveis acumulados em `UnitTests/bin` reduziram o espaço livre abaixo dos 500 MB exigidos pelo fixture. Identifiquei **369 diretórios** com prefixos emitidos pelos testes, confirmei que ficavam dentro da pasta de build e que não havia processos ativos, e removi somente esses fixtures sintéticos; isso liberou cerca de **80 GB**. A execução integral daquela repetição passou com **2.699 aprovados, 0 falhas e 20 ignorados** nos UnitTests, mais **43 aprovados** nos Benchmarks.

Após os incrementos de composição singleton, falha tipada para UTF-16 inválido e deadline não cooperativo no registry, a validação integrada foi repetida com `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=` e terminou com **0 avisos/0 erros**. Em seguida, `dotnet test EsilvaSoft.SlopStudio.slnx --no-build --no-restore` passou com **2.841 UnitTests aprovados, 21 ignorados e 0 falhas**, e **43 Benchmarks aprovados, 0 ignorados e 0 falhas**. A execução focada final passou com **142 aprovados e 1 ignorado**. O round-trip do Credential Manager continua ignorado por `1312 / NoLogonSession`; ele é um limite de homologação nativa registrado separadamente, não uma evidência positiva do Windows. Linux também continua sem D-Bus/Secret Service nativo exercitado. A auditoria de vulnerabilidades NuGet continua separada e indisponível pelo feed TLS/credenciais.

## Pendências da implementação, não da redação deste plano

## Incremento do registry interno — revisão de saída e composição — 23/09/2026

O provider/repository de políticas e `IAgentPermissionEvaluator` agora estão registrados no DI como singletons, compartilhando a instância existente de `LiteDbConnectionProfileRepository`. `IAgentToolRegistry` permanece sem registro e sem ingress MCP/chat, enquanto broker e controles de publicação ainda não existem.

Uma revisão crítica Astra encontrou risco de snapshot obsoleto em `list_connections` e de segredo arbitrário no campo livre `ConnectionProfile.Name`. O registry agora relê perfis após as avaliações assíncronas e falha fechado se ID, geração de origem, nome original, validade ou `IsReadOnly` do perfil autorizado mudar; revalida a política em seguida. Para `ProviderExternal`, o schema mantém `name`, mas envia `Conexão <ID>` determinístico e armazena o nome original somente no snapshot interno. Para `Local`, preserva o nome. A revalidação não é atômica com uma futura transmissão; o adapter ainda deverá checar a política/destino no ponto de publicação.

No Windows, o comando `dotnet test tests/EsilvaSoft.SlopStudio.UnitTests/EsilvaSoft.SlopStudio.UnitTests.csproj --no-restore -p:UsedAvaloniaProducts= --filter 'FullyQualifiedName~AgentAuthorizationServicesShareTheWorkspaceOwnerAndRemainSingletons|FullyQualifiedName~AgentPermissionEvaluatorTests|FullyQualifiedName~AgentToolRegistryTests|FullyQualifiedName~LiteDbAgentAuthorizationPolicyTests'` passou com **71 aprovados, 0 falhas e 0 ignorados**. O filtro cobre composição, evaluator, política persistida e registry, incluindo canários de URI/token/segredo arbitrário, alias local versus externo, mudança/remoção de perfil, falhas, deadline e cancelamento. Execução com `--no-restore` evitou o acesso negado ao NuGet.Config global; `-p:UsedAvaloniaProducts=` é a exceção ambiental prevista no AGENTS.md. `git diff --check` passou, com apenas avisos Git de normalização LF/CRLF. A prova nativa do Credential Manager foi executada separadamente e ignorada por `1312 / NoLogonSession`. Nenhum teste Linux foi executado nesta continuação, por solicitação do usuário.

Naquele incremento, isso confirmava somente os testes focados no Windows; não aprovava AC nem liberava tools. A revisão crítica identificou falha de correlação no ledger e a integração fail-closed ao registry ainda não existia. O limite CAS do repositório continua intra-owner, sem proteção contra escritores em processos distintos ou rollback de backup. O alias reduz o vazamento pelo nome, mas não homologa fluxo de saída externo.

## Integração da auditoria no registry interno — 23/09/2026

`AgentToolRegistry` agora exige `IAgentAuditRepository` no construtor. Para uma chamada `list_connections` com principal interno e contexto completos, grava uma intenção v2 antes de ler política ou perfis. Se a gravação falhar, bloqueia a operação e retorna `PermissionDenied` sem dados. Antes de entregar qualquer resultado, grava um desfecho correlacionado de sucesso, negação, falha, cancelamento ou prazo excedido; falha no desfecho também suprime a saída. O evento usa somente IDs técnicos, canal, tool, permissão, revisão, motivo tipado, horários e contagens. Não grava argumentos, resultado, texto de erro nem nome livre do perfil. O identificador externo vem do `ProviderId` validado contra o contexto, com alfabeto técnico restrito. O registry segue sem registro em DI e não há transporte externo.

No Windows, `dotnet test tests/EsilvaSoft.SlopStudio.UnitTests/EsilvaSoft.SlopStudio.UnitTests.csproj --no-restore -p:UsedAvaloniaProducts= --filter FullyQualifiedName~AgentToolRegistryTests` passou com **40 aprovados, 0 falhas e 0 ignorados**. Os casos novos cobrem bloqueio antes de ler política/perfil se o append de intenção falhar, supressão da saída se o append terminal falhar, correlação de sucesso e negação, cancelamento do chamador, prazo excedido e ID técnico de provider externo. O teste usa um repositório de auditoria em memória; a persistência LiteDB tem testes próprios e a execução combinada ainda precisa ser registrada.

O append terminal tem espera limitada de cinco segundos e observa falha tardia se o repositório ignorar cancelamento. Um timeout pode deixar intenção pendente porque o resultado da escrita tardia é incerto; nenhum resultado de tool é publicado nessa condição. O contrato de duração do evento limita a 30 segundos e uma chamada que ultrapasse esse limite também falha fechada, deixando a intenção para investigação. Ainda faltam broker autenticado, checagem no ponto de publicação pelo transporte, ferramentas de escrita, aprovação e demais gates. Nenhum teste Linux foi executado nesta continuação.

## Lote 1 — migração de credenciais Mongo, identidade de canal e scan — 24/09/2026

Tarefa P7-L01, executada por persistence-security-agent. Recorte: migração e recuperação de credenciais de perfis Mongo no proprietário LiteDB único, porta `IAgentPrincipalAuthority` e scan de segredos persistidos. Não foram alterados runtime, registry, broker nem UI; os arquivos de agentes de outros lotes também ficaram intactos.

- **Migração/recuperação:** journals com versão de esquema 1. Um registro gravado antes da existência do campo é lido como versão 1; versão desconhecida interrompe a recuperação com `InvalidDataException`, sem regravar o registro nem apagar segredo. `ILegacyConnectionCredentialMigration.ResumePendingAsync` retoma migrações interrompidas, remove gravações não confirmadas e limpa referências substituídas ou excluídas. `CountPendingCredentialRecoveryAsync` informa apenas a contagem. A exclusão do último perfil que usa uma referência agenda a limpeza no cofre na mesma transação. Foi corrigido um órfão: o journal de uma migração interrompida, superada por um salvamento protegido, agora é limpo. Com senha literal a proteger, salvamento, migração e verificação na conexão compartilham uma allowlist fechada de opções; antes, `retryWrites`/`w`/`appName` impediam salvar URIs com senha. Sem senha literal, a URI é persistida como foi digitada, e só são recusadas as opções que carregam segredo: senhas de chave/certificado, chaves que contêm *password/secret/token* e token AWS literal em `authMechanismProperties`. Correção A2 da revisão: `tlsCAFile`, X.509 e `readConcernLevel`, entre outras, voltam a salvar.
- **Retomada na inicialização (M5):** a composição do owner no DI dispara uma passada em segundo plano (`ResumePendingAsync` + `RecoverPendingChannelsAsync`) no mesmo owner, sem segunda conexão e sem bloquear a IDE. Falhas tipadas do cofre mantêm os registros na contagem. Um erro inesperado faz `CountPendingCredentialRecoveryAsync` (porta `IConnectionProfileCredentialStatusProvider`, agora registrada em DI para a UI) lançar mensagem fixa até uma passada posterior concluir; versão de journal desconhecida continua como `InvalidDataException`. Um salvamento protegido recupera antes a gravação interrompida do próprio perfil, de modo que o perfil não fica bloqueado depois de uma queda.
- **Identidade:** `IAgentPrincipalAuthority` é faceta do owner registrada em DI. `agentChannels` v1 guarda IDs, estado e referência. A prova de 256 bits fica só no cofre do SO, com comparação em tempo constante. O principal exige política persistida e fica vinculado à revisão dela; `IsCurrentAsync` serve para revalidar antes do despacho e da publicação. A revogação é gravada antes da remoção da prova, e uma revogação concorrente vence a autenticação. Um registro ilegível resulta em `Corrupt` e nunca é regravado.
- **Scan:** `FindCollectionsContainingAsync` percorre todas as coleções pelo owner, em texto bruto, percent-encoded e com escape JSON, e devolve só os nomes das coleções. O controle positivo encontra plaintext legado. O ciclo completo (migração, salvamento, duplicação, rotação, exclusão, recuperação e cadastro de canal) não deixa canários em documentos vivos. Salvamentos novos e a prova do canal também não aparecem nos bytes do arquivo (UTF-8/UTF-16LE).

Comandos no Windows, na revisão após as correções A2/M5 da revisão independente. Os números abaixo foram medidos nesta execução, sem reaproveitar contagens anteriores:

- `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=`: **exit 0, 0 avisos, 0 erros**. Foi preciso repetir o comando enquanto arquivos de outros agentes, em edição paralela, ainda não compilavam.
- `dotnet test tests/EsilvaSoft.SlopStudio.UnitTests --no-build --no-restore`, com filtro focado em credenciais, cofres, migração, inventário, perfis, DI, `LiteDb*`, recuperação, scan e principal: **exit 0; 313 aprovados, 0 falhas, 0 ignorados**. Inclui 14 testes novos de A2/M5.
- A execução "antes da correção" dos testes de A2 não pôde ser feita, porque o checkout estava sem compilar por arquivos alheios. A regressão está no código anterior: `ValidateQuery` rodava em toda URI, com ou sem senha.
- **Windows nativo:** round-trip do Credential Manager e prova ponta a ponta do perfil (salvar, resolver a URI, excluir e confirmar `NotFound`) aprovados, com dados sintéticos e limpeza em `finally`. Estão incluídos no filtro acima e não foram ignorados.

Isso não homologa segunda conta, roaming/backup, cofre bloqueado ou reinício do SO. Linux não foi executado, por instrução. Restos físicos de plaintext legado em páginas livres do LiteDB e em backups não são eliminados nem prometidos. A exibição da contagem na UI (ui-ux-agent) e a ligação de broker/runtime/registry ao principal ficam para os responsáveis desses lotes. Gate do lote 1: **parcial**; AC-03/07/08/17 continuam pendentes.

## Lotes 2, 3, 5, 6, 7, 8, 9 — reconciliação sobre os commits 5136141/21574b7/ebd8a49 — 25/09/2026 (PARCIAL)

Tarefa `P7-L12-DOC` (documentation-agent), a partir de auditoria read-only de code-review-agent. Confirmado nesta sessão com `git show --stat 5136141 21574b7 ebd8a49` e leitura pontual dos arquivos citados, no HEAD `b0c95e0` (worktree em `phase-7-i6v8dr`). Esta seção **não aprova nenhum AC nem gate**; seis agentes especialistas seguem trabalhando em paralelo nos itens marcados "outro agente cobrindo/corrigindo agora" e seus retornos ainda não haviam chegado quando esta seção foi escrita.

**Lote 2 — registry, EJSON literal, auditoria, quotas.** Avançou sobre o registrado em 23/09 (seções acima). `AgentToolRegistry` expõe 12 tools em três estágios fechados (`src/EsilvaSoft.SlopStudio.Application/Agents/AgentToolExposure.cs`, `AgentToolRegistry.cs:14-25`): `Metadata` (`list_connections`, `list_databases`, `list_collections`), `LiteralQueries` (`mongo_find`, `mongo_count`), `DerivedReads` (`get_collection_schema`, `sample_documents`, `mongo_find_one`, `get_document`, `mongo_distinct`, `get_indexes`, `mongo_explain`). `AgentToolLiteralEjson.cs` (656 linhas, commit `ebd8a49`) é o parser EJSON literal dedicado, sem `ENV`/construtores dinâmicos, com `AgentToolLiteralEjsonTests.cs` (155 linhas). `AgentToolInvocationQuota.cs` (commit `5136141`) reserva slots por conexão/sessão/global até a conclusão do backend, com `AgentToolInvocationQuotaTests.cs`. A auditoria segue fail-closed (intenção antes da política, desfecho correlacionado antes da saída), agora coberta também por `AgentToolRegistryGateTests.cs` (615 linhas). Falta, com outro agente cobrindo agora: composição incondicional do registry fora do `AddSlopStudioAgentBroker` opt-in; fixtures reais por tool do estágio `DerivedReads` contra MongoDB descartável (os testes atuais de `MongoAgentExplainSourceTests`/`MongoAgentIndexSourceTests`/`MongoAgentFindSourceTests` não fecham essa lacuna sozinhos); provider de produção para `IAgentSchemaSamplingConsentProvider`; e schema de auditoria completo frente ao contrato `09-seguranca-e-privacidade.md` (pendência já registrada em 23/09, ainda não fechada).

**Lote 3 — broker/proxy MCP.** `Agents/Broker/AgentBrokerEndpoint.cs`, `AgentBrokerFrameCodec.cs`, `AgentBrokerHost.cs`, `AgentBrokerConnection.cs` (Infrastructure/Infrastructure.Agents) e o projeto novo `EsilvaSoft.SlopStudio.McpServer` (`Program.cs`, `McpToolAdapter.cs`, `AgentBrokerClient.cs`) existem com testes (`tests/.../Mcp/AgentBrokerHostTests.cs`, `McpStdioProxyTests.cs`, `StdioMcpProcess.cs`, `RawBrokerPeer.cs`). `AddSlopStudioAgentBroker` (`src/EsilvaSoft.SlopStudio.Infrastructure/ServiceCollectionExtensions.cs:103-136`) é opt-in, exige `AgentBrokerOptions.Enabled`, e só libera até o estágio `LiteralQueries`. Falta: composição/inicialização no Desktop, cadastro de canal na UI, exercício contra MongoDB real via MCP (outro agente cobrindo agora) e decisão de gate formal para liberar `LiteralQueries` em produção.

**Lote 5 — AgentRuntime.** `AgentRuntime.cs` (refatorado para 463 linhas) mais os parciais `AgentRuntime.Turn.cs` (503), `AgentRuntime.State.cs` (238), `AgentRuntime.Interactions.cs` (655) e `AgentRuntimeEventQueue.cs` (216), todos do commit `ebd8a49`, implementam ciclo de vida de sessão/turno, fila de eventos limitada, aprovação e timeouts. Cobertura em `AgentRuntimeStreamTests.cs` (838 linhas) e `AgentRuntimeStreamTests.Review.cs` (452), `AgentRuntimeInteractionTests.cs` (249). O runtime não é instanciado em `src/` fora do assembly de testes; faltam autoridades de produção para `IAgentInteractionAuthority`/`IAgentContextProvider`/`IAgentPrincipalAuthority` real. Um bug de corrida cancelamento/dispose foi identificado nesta rodada (outro agente corrigindo agora); não tratar como corrigido nesta seção.

**Lote 6 — chat nativo.** `AgentChatPanel.axaml(.cs)`, `AgentApprovalWindow.axaml(.cs)`, `AgentSettingsWindow.axaml(.cs)` e os viewmodels (`AgentChatViewModel.cs`/`.Turn.cs`, `AgentApprovalViewModel.cs`, `AgentSettingsViewModel.cs`) existem com testes headless e PNGs (`AgentChatUiTests.cs` 444 linhas, `AgentChatViewModelTests.cs` 444, `AgentChatTestDoubles.cs` 282). Confirma-se `src/EsilvaSoft.SlopStudio.Desktop/Agents/AgentChatPorts.cs:6-11`: nenhuma implementação de produção está registrada, feature indisponível por padrão. `docs/17-design-system-ui-ux.md:288-290` já registrava isso corretamente e não foi alterado. Inspeção manual dos PNGs reais nos dois temas não foi feita nesta rodada de reconciliação documental, que tratou de composição/DI, não de validação visual; registrar como pendência explícita, não como aprovação implícita do lote.

**Lote 7 — OpenAI.** `OpenAi/OpenAiAgentProvider.cs`, `OpenAiAgentSession.cs`/`.Turn.cs` (Infrastructure.Agents) com `AgentProviderServiceCollectionExtensions.cs` oferecendo DI para o adapter — mas nada em `src/` chama essa extensão. Testes (`OpenAiAgentProviderTests.cs`, 593 linhas, `OpenAiFixtures.cs`, `OpenAiRealServiceTests.cs`) são offline, sem chamada real. AC-05/07/08/09/11/15/20 seguem pendentes.

**Lote 8 — Claude.** `Anthropic/ClaudeAgentProvider.cs`, `ClaudeAgentSession.Stream.cs`/`.Turn.cs`, `ClaudeToolCatalog.cs`, `ClaudeErrorCodes.cs` (Infrastructure.Agents) sem extensão de DI própria (outro agente adicionando agora). Testes offline: `ClaudeAgentProviderTests.cs` (340 linhas), `ClaudeAgentSessionTests.cs` (490), `ClaudeRuntimeIntegrationTests.cs` (160), com duplos em `ClaudeTestDoubles.cs`. AC-06/07/08/09/11/15/20 seguem pendentes.

**Lote 9 — provider local.** `LocalAgentProvider.cs`/`LocalAgentAvailability.cs` registrados e testados (`LocalAgentProviderTests.cs`, 431 linhas). Evidência de comportamento offline/preempção está sendo registrada agora por outro agente; não incluída nesta reconciliação.

**ACs.** Nenhum AC-01 a AC-20 é aprovado por esta seção. AC-17 (Codex App Server/decisão de confinamento) e AC-19 (escritas com precondição/replay) permanecem **NÃO IMPLEMENTADOS**. AC-18 (roadmap/links, lote 12) não é afetado por esta rodada de commits.

**Validação tentada neste worktree.** `dotnet build EsilvaSoft.SlopStudio.slnx --no-restore -p:UsedAvaloniaProducts=` terminou com **exit 155** antes de compilar qualquer projeto: o SDK `10.0.400` fixado em `global.json` não está instalado neste ambiente isolado (só `10.0.112` presente; `dotnet --list-sdks` confirma). É limitação deste worktree, não evidência de regressão introduzida pelos três commits — a validação build/test integral é responsabilidade dos seis agentes especialistas em seus próprios recortes e ambientes, cujos retornos ainda não haviam chegado nesta atualização.

## Pendências da implementação, não da redação deste plano

Fechar confinamento e armazenamento seguro Codex, incluindo decisão fundamentada sobre uso de App Server experimental; provar cofres reais em cada SO; validar clientes MCP externos e conformidade além do SDK sintético; observar streaming/tool calling Claude com credencial autorizada; implementar contratos/handlers/broker/UX; homologar MongoDB real, Windows/Linux e acessibilidade. Spikes de MCP e SDK Anthropic têm locks/inventário isolados, mas não substituem SBOM nem revisão de distribuição. Esses gates estão alocados no plano com critérios observáveis. A aprovação desta documentação não os encerra.

## Integração das rodadas paralelas e validação real no Linux — 25/09/2026

Validação no contêiner Linux da sessão com SDK `10.0.401` extraído localmente (o `10.0.400` de `global.json` não pôde ser instalado por bloqueio de rede; `global.json` não foi alterado) e MongoDB real 8.0.23. Antes das integrações, dois erros de analisador pré-existentes impediam o build oficial (`CA1822` em `AgentProviderCapabilities`, `CA1861` em `AgentProviderContractTests`) e foram corrigidos em `3a5917a`.

Integrados em série em `phase-7-i6v8dr`, com build e testes reais após cada merge: reconciliação documental (P7-L12-DOC), DI do Claude e recusa unificada de histórico (P7-L07/08), corrida cancelar/descartar no chat (P7-L05-FIX), testes reais `DerivedReads` e correção de `hasMore` (P7-L02-MONGO), registry e runtime sempre compostos com autoridades fail-closed e teste AC-14/AC-15 (P7-L02/05-ARCH), sessão e quota do broker, `Busy` na saturação e teste real broker→MongoDB (P7-L03-MCP), orçamento de fila do autocomplete em background (P7-L09-ONNX), wiring dos providers no Desktop com testes AC-04/AC-09/AC-15 (P7-L06-WIRING) e persistência do consentimento de amostragem (P7-L02-CONSENT, ainda fora do DI).

Resultado no HEAD `7e902f6`: build 0 erros; Infrastructure.Agents.Tests 87/0; Benchmarks 43/0; UnitTests 3.346 aprovados, 1 falha, 23 ignorados. A falha é o teste de latência `InlineGhostPresentationP95FromEditIsWithinTwentyMilliseconds`, instável neste contêiner (p95 de 20–38 ms contra limite de 20 ms; também falhava na base `b0c95e0`); asserção não alterada, pendente de confirmação em máquina de referência. Os lotes 10 (escritas) e 11 (hardening) foram interrompidos sem entrega. **Nenhum AC foi aprovado**; a lista de pendências está em [memória](../../memory/phase-7.md).
