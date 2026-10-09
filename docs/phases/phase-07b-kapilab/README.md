# Fase 7B — KapiLab: contratos e avaliação pelo caminho da IDE

**Situação:** planejada; meta documental criada em 09/10/2026.
**Meta:** F7B-KAPILAB.
**Versão:** ferramenta de desenvolvimento vinculada à Fase 8 / v0.12.0, sem release própria do produto.

## Objetivo

Planejar o console .NET KapiLab para gerar vetores sintéticos, renderizar contratos, extrair o catálogo seguro de tools e avaliar pacotes ONNX usando o código do EsilvaSoft.KapibaraStudio. Seus relatórios devem demonstrar qual caminho, pacote, versão e hardware foram exercitados.

A [meta de implementação](meta-de-implementacao.md) organiza os marcos K0–K4 e a [rastreabilidade](rastreabilidade-f7b.md) detalha 25 tarefas com dependências e aceite. K0+K1 formam o MVP; os demais marcos acrescentam amostras, paridade, agente, NPU e integração ao pipeline de avaliação local.

## Relação com as fases existentes

- [Fase 7A](../phase-07a-kapicoder-mongo/README.md): entrega contratos e capacidades do produto; 7B entrega os consoles e relatórios que os exercitam. As duas podem avançar por contratos, sem esperar encerramento integral uma da outra.
- [Fase 8](../phase-08-v0.12.0/README.md): mantém prioridade de IA local; KapiLab fornece evidências, sem substituir o aceite da IDE.
- [Fase 10](../phase-10-v0.14.0/README.md): concentra homologação real por pacote, hardware e plataforma.
- [Fase 7](../phase-07-v0.11.0/README.md): conserva seu workflow de chat. A numeração 7B identifica este trabalho de laboratório.

## Limites

Alvo arquitetural proposto: `tools/KapiLab/`, com referências relativas aos projetos existentes e testes próprios, sem UI ou dependência do Desktop. Treino, exportação/quantização, publicação de pesos e validação executável do dataset continuam no pipeline Python externo. Benchmarks e medições são manuais; nunca no CI ou na release.

O console usa fixtures sintéticas e não abre perfis, cofre ou LiteDB do usuário. Ferramentas continuam sujeitas às ADRs 066/067/070: nenhuma consulta direta de documentos ou escrita MongoDB por agente, inclusive no laboratório. Somente metadados e saídas sintéticas previamente capturadas podem compor as portas de teste.

## Estado desta entrega

Documentação e planejamento. Não há executável, dependência instalada, modelo carregado ou benchmark realizado por esta meta. A proposta arquitetural está registrada na ADR-071; decisões técnicas de implementação têm evidências exigidas na meta.
