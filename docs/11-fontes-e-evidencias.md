# Fontes e evidências

Consulta realizada em **07/09/2026**. Foram usadas fontes primárias: documentação oficial MongoDB, Microsoft, Avalonia, NUnit, repositórios dos componentes e páginas dos próprios produtos comparados. A página inicial do Driver C#/.NET foi reconferida nesta data e declara a linha 3.x como atual, com famílias para conexão, bancos/coleções, CRUD, agregações, índices, comandos, serialização e segurança. Não foi realizada uma bateria prática de benchmarks de concorrentes ou de servidores nesta etapa.

Os documentos anteriores distinguem fatos de referência, decisões propostas e testes pendentes. Links `current` e páginas de produtos são mutáveis; antes de implementar, registrar versão, data e evidência do tópico relevante no descritor de capacidade. Não reproduzir o manual integral dentro do repositório.

## Base e conexão

- [Driver C#/.NET solicitado pelo usuário](https://www.mongodb.com/pt-br/docs/drivers/csharp/current/) — ponto de partida e famílias de APIs.
- [Compatibilidade de bibliotecas](https://www.mongodb.com/docs/drivers/compatibility/) — cruzamento driver/servidor/runtime a completar na fixação dos pacotes.
- [MongoClient](https://www.mongodb.com/docs/drivers/csharp/current/connect/mongoclient/) — conexão pelo driver oficial.
- [Autenticação](https://www.mongodb.com/docs/drivers/csharp/current/security/authentication/) — mecanismos e restrições por edição.
- [Stable API](https://www.mongodb.com/docs/drivers/csharp/current/connect/connection-options/stable-api/) — opções e compatibilidade de comandos.
- [Release notes](https://www.mongodb.com/docs/manual/release-notes/) — evolução do servidor.

## Dados e linguagem

- [Comandos](https://www.mongodb.com/docs/manual/reference/command/) — inventário público e diferenças de suporte.
- [Predicados](https://www.mongodb.com/docs/manual/reference/mql/query-predicates/), [updates](https://www.mongodb.com/docs/manual/reference/mql/update/), [expressões](https://www.mongodb.com/docs/manual/reference/mql/expressions/) e [estágios](https://www.mongodb.com/docs/manual/reference/mql/aggregation-stages/) — catálogo do editor.
- [Tipos BSON](https://www.mongodb.com/docs/manual/reference/bson-types/), [Extended JSON v2](https://www.mongodb.com/docs/manual/reference/mongodb-extended-json/) e [GUIDs](https://www.mongodb.com/docs/drivers/csharp/current/serialization/guids/) — fidelidade dos documentos.
- [Validação](https://www.mongodb.com/docs/manual/core/schema-validation/) e [views](https://www.mongodb.com/docs/manual/core/views/) — estrutura lógica.
- [Transações](https://www.mongodb.com/docs/drivers/csharp/current/crud/transactions/) — sessões e operações sequenciais.
- [Limites](https://www.mongodb.com/docs/manual/reference/limits/) — validação de tamanho e restrições do servidor.
- [JavaScript no servidor](https://www.mongodb.com/docs/manual/core/server-side-javascript/) — depreciações.

## Índices, recursos avançados e administração

- [Índices no driver](https://www.mongodb.com/docs/drivers/csharp/current/indexes/) e [manual](https://www.mongodb.com/docs/manual/indexes/).
- [TTL](https://www.mongodb.com/docs/manual/core/index-ttl/) e [partial](https://www.mongodb.com/docs/manual/core/index-partial/).
- [GridFS](https://www.mongodb.com/docs/drivers/csharp/current/crud/gridfs/).
- [Limitações de time series](https://www.mongodb.com/docs/manual/core/timeseries/timeseries-limitations/).
- [Change streams no driver](https://www.mongodb.com/docs/drivers/csharp/current/logging-and-monitoring/change-streams/) e [servidor](https://www.mongodb.com/docs/manual/changeStreams/).
- [Criptografia em uso](https://www.mongodb.com/docs/drivers/csharp/current/security/in-use-encryption/).
- [Search/Vector Search próprios](https://www.mongodb.com/docs/search/self-managed/current/) e [anúncio de julho de 2026](https://www.mongodb.com/company/blog/product-release-announcements/mongodb-search-vector-search-now-run-anywhere).
- [Operações ativas](https://www.mongodb.com/docs/manual/reference/operator/aggregation/currentOp/) e [performance](https://www.mongodb.com/docs/manual/administration/analyzing-mongodb-performance/).
- [Configurar API Atlas](https://www.mongodb.com/docs/atlas/configure-api-access/).
- [Descontinuações App Services](https://www.mongodb.com/docs/atlas/app-services/deprecation/).

## Exportação e recuperação

- [mongodump](https://www.mongodb.com/docs/database-tools/mongodump/) — backup lógico, opções e limitações de oplog.
- [mongorestore](https://www.mongodb.com/docs/database-tools/mongorestore/) — restauração e controles de destino.
- [mongoexport](https://www.mongodb.com/docs/database-tools/mongoexport/) — exportação para intercâmbio.

## Stack e licença

- [LiteDB](https://www.litedb.org/docs/), [conexões Direct/Shared](https://www.litedb.org/docs/connection-string/), [criptografia](https://www.litedb.org/docs/encryption/) e [repositório/licença](https://github.com/litedb-org/LiteDB) — persistência local solicitada na revisão; versão fixada no arquivo Directory.Packages.props.
- [Scripts mongosh](https://www.mongodb.com/docs/mongodb-shell/write-scripts/), [EJSON](https://www.mongodb.com/docs/mongodb-shell/reference/ejson/) e [opções do executável](https://www.mongodb.com/docs/mongodb-shell/reference/options/) — runtime JavaScript + JSON existente em recurso antecipado/backlog. O protocolo e os helpers `slop` são contratos próprios da IDE, não APIs do fabricante.

- [Suporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy).
- [Plataformas Avalonia](https://docs.avaloniaui.net/docs/supported-platforms), [headless](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) e [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit).
- [NUnit em .NET](https://docs.nunit.org/articles/nunit/getting-started/dotnet-core-and-dotnet-standard.html).
- [MIT](https://opensource.org/license/mit), [licença Avalonia](https://github.com/AvaloniaUI/Avalonia/blob/main/licence.md) e [licença MongoDB Driver](https://github.com/mongodb/mongo-csharp-driver/blob/main/LICENSE.md).

## Limites e divergências encontrados

Alguns endereços antigos do driver redirecionam ou falharam na consulta. A documentação de GUID deve usar `/serialization/guids/` da linha 3.x; exemplos antigos de `GuidRepresentationMode` não devem ser transplantados sem revisão.

A referência geral de agregação ainda apresenta uma restrição Atlas para `$vectorSearch`, enquanto a documentação especializada e o anúncio recente descrevem suporte próprio. O plano prioriza a documentação especializada, identifica a divergência e exige homologação do serviço real.

A página dinâmica da especificação OpenAPI Atlas não forneceu conteúdo textual útil nesta consulta. A autenticação foi pesquisada na documentação de configuração; contratos endpoint a endpoint, campos de serviços recentes e limites de tiers serão extraídos da versão OpenAPI escolhida e testados em incremento futuro sem versão comprometida. Esta entrega não afirma ter validado cada endpoint.

## Processo de atualização

Antes de cada release, o responsável técnico revisa release notes do servidor/driver, mudanças de APIs Atlas, Avalonia e NUnit; atualiza o catálogo e roda os testes afetados. Para cada nova capacidade: inserir referência, condições, risco, UI/console, teste e changelog. Um teste de integridade impedirá publicar descritor sem classificação ou fonte.

Diferenças da documentação em relação ao servidor real devem virar caso reproduzível e item no registro de compatibilidade, sem habilitar funções por suposição. Recursos experimentais ou não testados ficam identificados como tais na interface e nas notas da versão.

## Fontes da revisão UI/UX

Consultadas na revisão de 09–10/09/2026: [Fluent 2 cores](https://fluent2.microsoft.design/color); [Fluent 2 tipografia](https://fluent2.microsoft.design/typography); [W3C contraste](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html); [Avalonia variantes](https://docs.avaloniaui.net/docs/styling/theme-variants).

A escolha da paleta e das dimensões é decisão de produto fundamentada nessas referências. Capturas automatizadas desta IDE usam dados sintéticos e ficam em ui-evidence no diretório dos testes. Não representam consultas executadas contra servidor real.

## MCP e agentes externos — pesquisa de 22/09/2026

O [registro específico de fontes/licenças](phases/phase-07-v0.11.0/README.md) documenta páginas oficiais consultadas para Codex App Server/SDK/API, Claude API/Agent SDK, autenticação de terceiros, MCP 2026-07-28 e compatibilidade 2025-11-25, Credential Manager e Secret Service. Versões móveis e licenças de candidatos não são homologação de pacote ou integração; fixação de artefatos/transitivas permanece gate da implementação.
