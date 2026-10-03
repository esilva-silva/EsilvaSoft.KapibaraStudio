# Releases arquivadas

Esta pasta contém os requisitos **concluídos** de cada versão, com a evidência disponível no momento do arquivamento. Trabalho planejado ou em execução fica em [`../phases`](../phases/README.md); requisitos adiados ou sem fase ficam em [`../backlog`](../backlog/README.md).

| Release | Escopo | Estado do arquivamento |
| --- | --- | --- |
| [`release_v0.5.0`](release_v0.5.0/README.md) | MVP: conectar → navegar → consultar → visualizar → editar → exportar | Escopo funcional fechado. Validação manual transferida para a [Fase 10 / v0.14.0](../phases/phase-10-v0.14.0/README.md). |
| [`release_v0.6.0`](release_v0.6.0/README.md) | Organização dos projetos e autocomplete básico | Escopo funcional fechado; homologação manual transferida para a [Fase 10 / v0.14.0](../phases/phase-10-v0.14.0/README.md). |
| [`release_v0.7.0`](release_v0.7.0/README.md) | Autocomplete com IA explícita | Escopo funcional fechado; homologação manual transferida para a [Fase 10 / v0.14.0](../phases/phase-10-v0.14.0/README.md). |
| [`release_v0.8.0`](release_v0.8.0/README.md) | Arquivos de texto e workspace local de uma pasta | Escopo funcional fechado; homologação nativa transferida para a [Fase 10 / v0.14.0](../phases/phase-10-v0.14.0/README.md). |

## Regra de arquivamento

Arquivar uma versão registra que **o escopo funcional foi implementado e revisado**. Não afirma publicação de release. A homologação manual aplicável é consolidada na Fase 10; o documento `pendencias-de-homologacao.md` de cada release preserva apenas o registro da transferência e as evidências históricas.

Teste automatizado e execução Headless **não** encerram pendência de homologação real.
