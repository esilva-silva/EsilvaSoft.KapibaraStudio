# MVP do EsilvaSoft.KapibaraStudio

Referência: 06/11/2026. **Studio — MongoDB Database Management & Development Tool**.

Esta é a especificação oficial do MVP em `docs/`. Contratos e evidências necessários estão consolidados nesta árvore, sem consulta obrigatória ao acervo removido. A criação desta documentação não altera código, disponibilidade, versão do binário, tags ou pacotes.

O MVP concentra gestão cotidiana de bancos e desenvolvimento com Console JavaScript, preservação de BSON, segurança e assistência opcional. O recorte aprovado corresponde aos quinze itens do [catálogo de requisitos](02-requisitos-e-estado.md). Os cortes foram implementados na Task-R 1, com compatibilidade e testes por lote; a homologação real permanece separada, conforme [evidências](../Remover/TODO.md).

## Leitura e responsabilidade

- [Produto e escopo](01-produto-e-escopo.md): limites e destinos dos cortes e melhorias.
- [Requisitos e estado](02-requisitos-e-estado.md): fonte única do estado dos MVP-001..015.
- [Arquitetura e invariantes](03-arquitetura-e-invariantes.md): contratos obrigatórios.
- [Design system](04-design-system.md) e [guia](05-guia-de-uso.md): comportamento e experiência.
- [Validação](06-validacao-e-evidencias.md), [distribuição](07-distribuicao-e-suporte.md) e [decisões](08-decisoes-vigentes.md).
- [Contratos de agentes](contratos/tools-agentes.md), [arquivos](contratos/arquivos-agentes.md) e [IA local](contratos/ia-local.md): entradas, limites e recuperação.
- [Baseline de validação](09-baseline-de-validacao.md): resultados datados, procedimentos e limitações consolidados.
- [Fases](fases/README.md): entregas futuras verificáveis e critérios de saída.

Cada requisito possui um estado de implementação e outro de validação. Implementado não significa homologado. Os TODOs das fases começam desmarcados porque representam aceite no novo plano, sem apagar aceites históricos.

A prioridade é concluir IA local CPU e depois Copilot. A preparação de limpeza/base não deve se tornar uma revisão ilimitada que bloqueie essa ordem.
