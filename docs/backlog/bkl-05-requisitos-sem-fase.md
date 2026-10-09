# Backlog — requisitos sem fase comprometida

Consolida os requisitos que não têm versão atribuída no [roadmap](../09-plano-de-implementacao.md). Os IDs do [catálogo funcional](../03-catalogo-funcional.md) são preservados; o [inventário](../24-inventario-roadmap.md) registra a evidência em código de cada recorte parcial.

## 📋 Planejado — sem caminho integrado

| IDs | Assunto |
| --- | --- |
| CON-04/05 | Autenticações especializadas e SSH |
| DAT-07/12 | Bulk multinamespace e transações |
| EDT-05/07 | Console BSON genérico e exportação C# de query |
| AGG-02 | Apoio visual à construção de pipeline — ver [bkl-04](bkl-04-modo-aggregation.md) |
| IDX-05/06 | Gestão de Search/Vector Search e query settings |
| TRF-04/06/07 | Database Tools (backup/restauração), sincronização revisável com checkpoint e agenda |
| ADM-06/07/08/10 | Administração distribuída, sharding/replicação e integração Atlas |
| ADV-01…ADV-07 | GridFS, séries temporais, change streams, criptografia em uso e integrações especializadas |

Menção a um comando no vocabulário do editor **não** comprova suporte administrativo.

## 🚧 Parcial — recorte existente, requisito aberto

- **ADM-05** — topologia por `hello`, sem diagnóstico de lag, oplog ou failover.
- **ADV-08** — inferência de schema por amostra, sem SQL, gerador ou migrações versionadas.
- **ADV-09 (parcela Git)** — continua planejada; Claude, Codex, APIs externas e integração MCP externa estão no [bkl-06](bkl-06-integracoes-agentes.md), com implementações preservadas.
- **TRF-05** — pode ser expresso por script entre conexões, mas não há assistente de clonagem com retomada. Ver [bkl-03](bkl-03-script-engine-entre-conexoes.md).

## Requisitos movidos para o backlog nesta reorganização

| Requisito | Documento |
| --- | --- |
| Cofre criptográfico de ambientes (recorte de CON-07) | [bkl-01](bkl-01-key-vault-criptografico.md) |
| Recortes não priorizados da janela de Ferramentas | [bkl-02](bkl-02-ferramentas-fora-de-fase.md) |
| Script Engine entre conexões (EDT-06, TRF-05) | [bkl-03](bkl-03-script-engine-entre-conexoes.md) |
| Modo Aggregation (AGG-01/03/04) | [bkl-04](bkl-04-modo-aggregation.md) |
| Demais integrações externas de agentes (ADV-09) | [bkl-06](bkl-06-integracoes-agentes.md) |
| Recortes ADM-01/04/09 que exigem acesso fora da connection string (08/10/2026) | [bkl-07](bkl-07-administracao-fora-da-connection-string.md) — métricas externas, controle gerenciado, startup/configuração persistente e manutenção de SO |

## Regras

Capacidades de serviços externos são escopo técnico futuro, **não** recomendação de aquisição nem autorização de nova dependência comercial. Cada incremento exige decisão de escopo, dependências e licenças, ambiente, segurança, critério observável e evidência própria. Nenhum item deste backlog bloqueia automaticamente a v1.0.0.
