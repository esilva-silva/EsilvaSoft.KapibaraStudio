# Registro de transferência de homologação — v0.6.0

A release [v0.6.0](README.md) foi arquivada por escopo funcional. As pendências abaixo continuam abertas e foram transferidas para a [Fase 10 / v0.14.0](../../phases/phase-10-v0.14.0/README.md); nenhum teste Headless ou registro documental encerra esses itens.

## Autocomplete e editor

- executar o job completo de latência p95/p99 e repetir os cenários na plataforma de referência;
- resolver ou aceitar formalmente a alocação acima de 64 KB com catálogos grandes e o highlighting de 64 KiB acima do orçamento;
- completar a evidência independente de `TypeMismatchPenalty`, da regra Elo `Stage → GroupBody` e da matriz visual dos 18 cenários nos dois temas;
- homologar `Ctrl+Espaço`, IME e layouts ABNT2/US em Windows, X11/Wayland em Linux, além de leitor de tela e foco/teclado nativos.

## Integração externa

- verificar metadata e schema em MongoDB real, isolamento entre perfis e recuperação sob desconexão;
- repetir o fluxo completo em Linux e em arquiteturas fora de Windows x64;
- validar acessibilidade e composição visual com a aplicação nativa, incluindo a colisão documentada de `Ctrl+Espaço` com troca de IME.

As evidências automatizadas, seus limites e a distinção entre recorte funcional e requisito amplo estão em [README](README.md), na [matriz de validação](../../15-matriz-de-validacao.md) e no [checklist de homologação](../../16-checklist-homologacao.md).
