# Visão e escopo

## Objetivo

Criar uma IDE desktop que permita ao desenvolvedor consultar e editar dados com precisão e ao administrador diagnosticar e operar MongoDB com contexto suficiente para reconhecer o impacto de cada ação.

Requisitos fixos: nome `EsilvaSoft.SlopStudio`, C#/.NET 10, Avalonia, persistência local LiteDB, MIT, documentação em `docs`, testes unitários NUnit e suporte a **Windows e Linux**. Interpretamos “ID” como IDE. Ambos os sistemas participam do desenvolvimento, CI e homologação desde a fundação. O modo script deverá executar JavaScript junto de queries JSON na mesma execução, com variáveis, funções, controle de fluxo e chamadas MongoDB.

## Usuários e jornadas

| Perfil | Jornada principal | Resultado esperado |
| --- | --- | --- |
| Desenvolvedor | Conectar, explorar, consultar, editar e salvar script | Alteração tipada, reproduzível e com resultado compreensível |
| DBA/SRE | Ver topologia, operações, métricas e índices | Diagnóstico e ação com permissões e impacto identificados |
| Analista | Filtrar, agregar e exportar | Dados exportados com formato e limites declarados |
| Equipe de suporte | Alternar ambientes e investigar incidentes | Contexto visível e credenciais protegidas |
| Responsável por migração | Comparar, copiar, restaurar e verificar | Relatório de divergências e execução auditável |

## Produto mínimo utilizável

O MVP é a **Fase 1 / v0.5.0**: conectar → navegar → consultar → visualizar → editar → exportar. Inclui Explorer, find/findOne/filtro/sort/limit/skip, CRUD BSON protegido, autocomplete simples, formatação JSON/query/script e exportação JSON/CSV. CSV da página, formatação explícita e status global concorrente têm implementação e evidências na [auditoria de polimento](done/release_v0.5.0/25-auditoria-mvp-performance.md). A homologação Windows/Linux é acompanhada na Fase 9 / v0.13.0.

A **v0.6.0** consolida organização e autocomplete determinístico; a **v0.7.0**, autocomplete com IA; a **v0.8.0**, arquivos de texto e workspace local; a **v0.9.0**, IA local; a **v0.10.0**, administração e manutenção; e a **v0.11.0**, Claude e agentes. A integração Claude usa Claude Code oficial, mantém API separada e está em validação no Windows; veja [Claude no KapibaraStudio](phases/phase-07-v0.11.0/README.md). A **v0.12.0** trata de chat por workflow. A **v0.13.0** concentra homologação manual; a **v1.0.0** fecha estabilidade, instalação e atualização. IA local continua independente.

Versão identificável no repositório: tag alpha local **v0.1.1-alpha**, seguida de desenvolvimento; não se afirma publicação remota. Consulte o [roadmap](09-plano-de-implementacao.md) para dependências/exclusões/aceite e o [inventário](24-inventario-roadmap.md) para evidência e backlog sem versão comprometida.

## Princípios de produto

- Tipos e bytes persistidos têm prioridade sobre apresentação conveniente.
- Toda aba mostra conexão, ambiente, banco e coleção; mudar a seleção do explorer não muda o destino de uma aba existente. O destino é contexto da aba, não um campo repetido no formulário da consulta.
- Leituras, exportações e alterações longas são tarefas observáveis, com limites e cancelamento.
- O servidor decide autorização; o aplicativo acrescenta contexto e proteção contra enganos.
- Consultas são escritas no editor textual. Filtro, projeção, ordenação, limite, paginação, hint, collation e demais parâmetros pertencem ao texto executado, com autocomplete contextual e diagnóstico local.
- Formulários são reservados às operações administrativas e de configuração que não são consultas; não há um construtor de consulta com múltiplos campos concorrendo com o editor.
- A interface continua útil com privilégios reduzidos e metadados parciais.
- Consultas e dados não são enviados a provedores de IA por padrão.

## Fronteiras

A abstração de provedores permitirá outros bancos no futuro, mas o primeiro provedor é exclusivamente MongoDB oficial. DocumentDB e Cosmos DB não receberão selo de compatibilidade sem testes próprios. Não haverá ORM obrigatório para documentos arbitrários.

Operações como instalar servidores, configurar discos, fazer snapshots do volume, editar `mongod.conf`, iniciar serviços ou administrar Kubernetes exigem conectores próprios ou execução externa. A primeira cobertura será inventário, diagnóstico e runbooks. O aplicativo não prometerá executar essas ações via driver C#.

SQL, integrações Git e conectores permanecem no backlog sem versão comprometida. IA local tem implementação experimental, consolidada na v0.9.0; não é dependência do autocomplete determinístico. Serviços externos não se tornam dependências comerciais sem decisão específica.

## Base tecnológica e versões

✅ Implementado: solução .NET 10, projetos Core/Application/Infrastructure/Desktop e testes NUnit. O `global.json` fixa SDK 10.0.400 com rollForward latestFeature; Directory.Packages.props registra Avalonia 12.1.2, AvaloniaEdit 12.0.0, MongoDB.Driver 3.11.1, LiteDB 5.0.21 e NUnit 4.6.1. Lockfiles preservam dependências reproduzíveis. Esses números foram conferidos nos arquivos locais, não são recomendações de versões mais recentes.

🚧 Em desenvolvimento: homologação integral Windows/Linux, matriz de versões/topologias MongoDB, instalação/atualização e inventário de capacidades. As páginas oficiais mutáveis não substituem testes com as versões fixadas.
