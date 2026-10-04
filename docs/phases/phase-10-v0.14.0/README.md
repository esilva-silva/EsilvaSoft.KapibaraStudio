# Fase 10 — v0.14.0: homologação manual e validação em ambientes reais

**Situação:** Planejada.

## Objetivo

Concentrar a validação manual antes distribuída nas fases funcionais, sem adicionar funcionalidades de produto nem reabrir seus critérios automatizados.

## Escopo incluído

- Homologação em Windows e Linux gráficos: gerenciadores de janela, teclado, IME, clipboard, diálogos nativos de arquivo/pasta e lixeira.
- Acessibilidade com leitor de tela, foco e navegação por teclado nas jornadas alteradas.
- MongoDB e `mongosh` reais: autenticação, TLS/X.509, permissões, somente leitura, topologias, operações destrutivas, edição concorrente e metadados.
- Modelos e hardware reais: CPU/GPU/NPU, fidelidade de ações, revisão linguística de domínio, latência e memória.
- Avaliação presencial das jornadas com representantes de desenvolvimento, operação e análise, registrando amostra e limitações.
- Instalação, atualização e recuperação em máquinas limpas; integridade dos artefatos e evidência sem credenciais ou payloads sensíveis.
- Registro datado dos ambientes e resultados na [matriz de validação](../../15-matriz-de-validacao.md) e no [checklist](../../16-checklist-homologacao.md).

## Fora de escopo

Implementar recursos novos, alterar critérios automatizados já aceitos, reescrever o histórico de evidências ou declarar suporte para ambiente não exercitado.

## Critério de aceite

Executar e registrar os cenários aplicáveis do checklist em seus ambientes reais, com sistema operacional, versões relevantes, configuração sem segredos e resultado observável. Cada alegação de suporte em plataforma, hardware, acessibilidade, instalação ou topologia deve apontar para essa evidência.

## Dependências

Critérios funcionais automatizáveis das Fases 1 a 8, polimento da [Fase 9 / v0.13.0](../phase-09-v0.13.0/README.md), artefatos de distribuição e ambientes de teste disponíveis.

## Documentos relacionados

- [Checklist de homologação](../../16-checklist-homologacao.md) · [Matriz de validação](../../15-matriz-de-validacao.md)
- [Fase 5 — Copilot](../phase-05-v0.9.0/README.md): homologação aplicável de conta, conversa, ferramentas com MongoDB de teste, sessões, propostas e UI nativa; respeitar o limite de validação Linux registrado na meta.
- [Demais integrações de agentes — backlog](../../backlog/bkl-06-integracoes-agentes.md): roteiros e gates próprios são preservados para eventual retomada; sua conclusão não é compromisso da Fase 5 nem entra automaticamente no aceite da Fase 10. Regressões compartilhadas e segurança dos caminhos já disponíveis continuam sujeitas à validação.
- [Fase 1](../phase-01-v0.5.0/README.md) · [Fase 2](../phase-02-v0.6.0/README.md) · [Fase 3](../phase-03-v0.7.0/README.md) · [Fase 4](../phase-04-v0.8.0/README.md) · [Fase 8](../phase-08-v0.12.0/README.md) · [Fase 6](../phase-06-v0.10.0/README.md) · [Fase 7](../phase-07-v0.11.0/README.md)
