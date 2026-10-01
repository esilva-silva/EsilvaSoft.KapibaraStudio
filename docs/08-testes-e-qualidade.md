# Testes NUnit e qualidade

**Estabilidade — 01/10/2026:** [diagnóstico, correções e boas práticas](architecture/test-stability.md). Os dois sistemas executam a mesma lista explícita de suítes unitárias; a guarda estática de fontes também roda no Linux. Benchmarks e testes das ferramentas de medição exigem execução manual de desenvolvimento e ficam fora de CI/release. Prazos de produto usam relógio controlado nos testes; conclusão de tarefas substitui pausas fixas nos cenários revisados de sessão, metadados e ghost.

## Estratégia

### Isolamento do sistema operacional — meta ativa em 30/09/2026

Testes unitários devem consumir interfaces com mocks ou fakes controlados, sem iniciar processos/PowerShell, acessar console real, arquivos, diretórios, cofre, banco ou rede. Regras puras permanecem no projeto unitário; testes dos adapters reais pertencem à integração, com recursos sintéticos exclusivos e limpeza segura. `TestSupport` fornece doubles em memória e depende apenas de `Application`. Os adapters locais extraídos ficam em `Infrastructure.System`.

A migração está incompleta: o nome `UnitTests` e a categoria histórica de uma fixture não garantem isolamento. O contexto compartilhado já não usa LiteDB; contextos privados, SDKs, renderização e processos ainda exigem revisão. Consulte o [plano e inventário](architecture/system-adapters.md) e a [ADR-060](10-decisoes-arquiteturais.md#adr-060--adapters-do-sistema-operacional-e-isolamento-dos-testes-30092026). O filtro de categoria só deve ser considerado gate de isolamento após a classificação completa.

`CopilotProductToolManualTests` está em IntegrationTests com categorias `Integration`/`OfficialManual` e testes `Explicit`; exige conta oficial e seleção nominal consciente. A execução automática da integração não chama modelos oficiais. Seus diretórios são sintéticos exclusivos e o provider de teste captura a pasta da sessão para cleanup. Doubles compartilhados em TestSupport não iniciam SDK/armazenamento. Os testes de regras de `NativeChatTurnPolicyProvider` agora usam políticas/autoridade em memória; isso não homologa persistência ou emissão de identidade do adapter real.

`AgentSessionToolsTestRig` usa diretórios, bytes e links em memória pelos contratos de reader/probe; seus 35 casos não criam arquivos nem verificam timestamps reais. Preservação da fonte é observada no estado simulado. A validação real de bytes/BOM/base modificada do store está na integração. Fixtures de regras injetam callback de publicação de eventos para evitar inicializar dispatcher Avalonia; testes Headless continuam usando a sessão de UI própria. Essa extração não classifica automaticamente os demais testes do projeto como isolados.

Todos os testes .NET usarão **NUnit**, incluindo testes unitários, integração e UI. Pacotes propostos: `NUnit`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk` e `NUnit.Analyzers`, com versões fixadas em Directory.Packages.props. Usar inicialmente o caminho VSTest do `dotnet test`; migração para outro runner exige configuração e validação deliberadas. O nome NUnit3TestAdapter não implica uso obrigatório de NUnit 3. [Execução .NET com NUnit](https://docs.nunit.org/articles/nunit/getting-started/dotnet-core-and-dotnet-standard.html).

Na primeira implementação, a suíte NUnit foi criada e já valida modelos, LiteDB e o parser de resultados do modo script. A matriz abaixo continua sendo o escopo de qualidade a repartir pelos gates v0.5.0–v1.0.0; resultados atualizados ficam no [acompanhamento](12-acompanhamento-da-implementacao.md).

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx -c Release --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx -c Release --no-build --filter "TestCategory=Unit"
dotnet test EsilvaSoft.KapibaraStudio.slnx -c Release --no-build --filter "TestCategory=Integration"
dotnet test EsilvaSoft.KapibaraStudio.slnx -c Release --no-build --filter "TestCategory=UI"
```

A fundação já contém lockfiles para `--locked-mode`. Testes de arquitetura terão categoria própria e execução obrigatória na CI. Logs TRX e cobertura serão artefatos locais/CI, saneados antes de publicação.

## Matriz unitária

Testes Claude com subprocesso falso e respectivas fixtures JSONL foram separados em IntegrationTests/ClaudeCode. Testes MCP de named pipe/proxy/broker e composição com LiteDB estão em IntegrationTests/Mcp; os dois casos puros de admissão/opções permanecem na unidade. Os sete partials de Mongo real passaram à integração com execução explícita. ConsoleRuntimeTests usa repositório/sessões em memória; o bootstrap mantém uma única fonte textual C# sem leitura de recurso. A [auditoria semântica](architecture/unit-resource-audit.md) registra os grupos restantes, incluindo renderização e fixtures privadas LiteDB.

O catálogo do picker e a existência de arquivos do editor são simulados por interfaces. Sete testes do picker cobrem busca, limites, recusas, snapshot, cancelamento e respostas antigas; três testes de privacidade usam arquivos em memória. `AgentArchitectureTests` conserva três verificações de metadados de assemblies. A leitura/enumeração do código-fonte foi separada em `AgentSourceArchitectureTests`, categoria `Integration`/`ArchitectureSource`; essas verificações ainda não constituem proteção semântica completa contra I/O transitivo.

| Grupo | Requisitos | Casos críticos |
| --- | --- | --- |
| Perfis/segredos | CON-01/02/07/08 | URI escapada, SRV, authSource, revisão, export sem segredo, perfil duplicado |
| Persistência LiteDB | CON-01/07, EDT-04/06, UX-02 | Mapeamento de DTOs, versão de schema, índices locais, retenção e payload BSON opaco |
| Capacidades | CON-09, ADM, ADV | Versão/FCV divergentes, permissão desconhecida, topologia e serviço ausente |
| BSON | EDT-01/03 | Int64 acima de 2^53, Decimal128, null/ausente, timestamp, binário e campos especiais |
| UUID | EDT-03 | Vetores independentes de bytes, subtype 3/4, strings, zero e formatos mistos |
| Linguagem | EDT-02/04, AGG-03 | Texto incompleto, escopos, aliases, Unicode, cancelamento e cache por conexão |
| Escrita | DAT-04/05/06/08 | Projeção parcial, `_id`, concorrência, arrays e filtro vazio |
| Índices | IDX-01/02/03 | Ordem de chaves, opções incompatíveis, TTL inválido e recriação explícita |
| Transferência | TRF-01/02/03 | Streaming, escaping CSV, tipos, chunks, arquivo parcial e manifesto |
| Processos | TRF-04, EDT-06 | Argumentos com espaços, segredos, stderr cheio, timeout e exit code |
| Script JavaScript + JSON | EDT-02/03/06 | Contexto/entradas, parâmetros sem interpolação, envelopes EJSON, erro com linha original, isolamento entre execuções e limites |
| Jobs | UX-02, TRF-07 | Cancelamento, resultado incerto, retry proibido e recuperação após reinício |
| Segurança | ADM-11 | Sanitização de URI/token/chave, modo leitura e `$out`/`$merge` |
| ViewModels | UX-01, DAT | Destino fixo por aba, validação, seleção, navegação e erros independentes |

Usar fixtures determinísticas, relógio injetável e fakes de contratos do aplicativo. Evitar simular detalhes internos do driver e concluir que isso demonstra comportamento MongoDB. Testes parametrizados com `TestCaseSource` cobrem representações BSON. Geração aleatória deve registrar seed e reduzir casos de falha.

## Integração real

| Ambiente | Evidência exigida |
| --- | --- |
| Standalone | CRUD, índices, autenticação e rejeição de recursos de replica set |
| LiteDB em disco, Windows/Linux | Fechar/reabrir perfis e scripts, índice único, rollback de migração, segunda instância, arquivo indisponível e recuperação de cópia consistente |
| mongosh + MongoDB, Windows/Linux | Executar o exemplo JS + JSON, loops/funções/await, múltiplas queries, parâmetros EJSON, UUID/Int64/Decimal128, resultados estruturados e cancelamento |
| Replica set de um membro | Transações básicas, change streams e dump com oplog |
| Replica set de três membros | Eleição, retry, disconnect, lag e resultado de escrita incerto |
| Cluster sharded | Roteamento, shard key, administração e limites de backup |
| TLS e usuários restritos | Certificado válido/inválido, autenticação, permissão negada e isolamento |
| Search/Vector Search | Criar índice, esperar pronto e executar consultas quando disponível |
| Enterprise/KMS | Criptografia e mecanismos de autenticação efetivamente homologados |
| Atlas dedicado a testes | API, paginação, permissões, orçamento e limpeza de recursos identificados |

Containers devem usar tags e preferencialmente digests fixos, startup com health checks e replica set iniciado antes dos testes. Um container MongoDB simples não comprova transações. Cada teste usa namespace exclusivo; limpeza só remove recursos criados pelo próprio teste, identificados por prefixo e ID. Credenciais vêm do ambiente de CI, nunca do repositório.

Casos obrigatórios: duplicate key, falha de validação, timeout, perda de rede, cancelamento durante escrita, bulk parcialmente aplicado, unique/partial/TTL, `$out` impedido em prévia, documento concorrente, token de change stream expirado e restauração completa de fixture BSON.

Para transações, comprovar abort e commit, repetição controlada do callback e ausência de efeitos externos duplicados. O driver não permite operações paralelas na mesma transação. [Transações](https://www.mongodb.com/docs/drivers/csharp/current/crud/transactions/).

Fixtures de backup usam tipos heterogêneos e UUIDs de bytes conhecidos, índices e validadores. Verificar restauração com consultas diretas e metadados do servidor. Teste não deve comparar apenas texto JSON ou contagem de documentos.

Para LiteDB, complementar testes locais em memória com arquivo real e processos separados nos dois sistemas. Validar APIs e versões escolhidas em .NET 10, proteção por cofre e falha durante migração/rebuild. Não considerar o `BsonDocument` do LiteDB equivalente ao do MongoDB: testar ida e volta de payloads MongoDB com fixtures independentes.

Para script, testar mongosh ausente/incompatível, erro de sintaxe, erro após uma escrita, loop infinito, saída excessiva, processo que termina inesperadamente, caminhos com espaços/Unicode e credenciais TLS. O canal estruturado deve manter tipos mesmo com `printjson` e stderr intercalados; código impresso nunca provoca execução na IDE. Testar execução da seleção sem variáveis anteriores e separação entre abas. Esses cenários permanecem no backlog do modo script; não são critérios da v0.8.0.

## UI e desempenho

Usar `Avalonia.Headless.NUnit` para foco, teclado, bindings, validação e comandos. Ele permite testes sem janela, mas não substitui verificação de renderização, leitor de tela, diálogos nativos e empacotamento em cada SO. [Headless Avalonia](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform).

Metas propostas, a calibrar antes do aceite de performance com máquina e dataset registrados:

- Sugestões em cache: p95 até 100 ms, sem rede no caminho crítico.
- Ação local comum: p95 até 100 ms de processamento na UI.
- Primeira página de consulta indexada local: p95 até dois segundos, discriminando tempo de servidor.
- Exportação de dez milhões de documentos: memória adicional estabilizada abaixo de 256 MiB para documentos de aproximadamente 1 KiB; medir baseline separadamente.
- Dez conexões e vinte abas abertas: nenhuma tarefa bloqueia outras por estado global compartilhado.
- Cancelamento: UI sinaliza pedido em até 250 ms; término remoto não possui garantia universal.

Esses números são metas de produto, não benchmarks obtidos. Executar cargas maiores e documentos grandes para comprovar limites em bytes, além de contagens. Comparações de desempenho precisam da mesma configuração de servidor e índices.

## Portões de qualidade

PR: build Release, analisadores, testes unitários/arquitetura, integração dos módulos afetados e verificação de segredos/licenças. Meta inicial de cobertura: 80% de branches em Core/Application/Editor, com casos explícitos para caminhos críticos; cobertura agregada não substitui cenários de perda de dados.

Execução noturna planejada: matriz ampliada de versões/topologias, falhas de rede e cargas; isso será CI do projeto, não uma automação criada nesta conversa. Release: restore real, pacotes em máquinas limpas, UI em Windows e Linux, SBOM, notas e ausência de defeitos críticos abertos. Ambos os sistemas executam build/test desde a fundação; v0.5.0 exige persistência/fluxo básico e v0.8.0 exige arquivos/workspace local. O modo script permanece fora desta release.

Teste dependente de infraestrutura ausente deve aparecer como não executado com motivo. Não declarar Enterprise/Atlas homologados a partir de mocks. Testes instáveis terão responsável e prazo; não remover silenciosamente da suíte obrigatória.

## Testes da revisão UI/UX

WorkspaceBehaviorTests verifica isolamento entre abas, conclusão fora de ordem, seleção sem fallback, cancelamento, somente leitura, perfil alterado, migração, recuperação, descarte, opt-in e falhas de persistência. WorkspaceRenderingTests usa Avalonia.Headless com Skia, os recursos reais da aplicação e mocks de MongoDB.

O teste gera 18 imagens de workspace por sistema (2 temas × 3 dimensões × 3 escalas), além da modal. Verifica dimensões úteis do editor, foco inicial, Escape da modal durante execução e alternância por teclado. Inspecionar as imagens; build/NUnit sozinhos não provam legibilidade. A integração com servidor, mongosh, leitor de tela e seletores de arquivo nativos exige homologação própria.

## Claude e agents

Os testes automatizados cobrem contratos, autenticação reportada pela CLI, streaming, retomada, aprovações e isolamento de sessão; não substituem a homologação com Claude Pro real nem MongoDB real. Use o roteiro e registre os resultados reais na [documentação Claude](phases/phase-07-v0.11.0/README.md) e na [matriz de validação](15-matriz-de-validacao.md).
