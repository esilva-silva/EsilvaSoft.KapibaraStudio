# Registro de transferência de homologação — v0.7.0

A release [v0.7.0](README.md) foi arquivada por escopo funcional. As verificações que precisam de ambiente real foram transferidas para a [Fase 10 / v0.14.0](../../phases/phase-10-v0.14.0/README.md); a transferência não as encerra.

- Executar `Ctrl+;`, fallback sem modelo e cancelamento em Windows e Linux gráficos, com layouts ABNT2/US e IME reais.
- Exercitar leitor de tela, foco, atalhos e prévia/indicador com tecnologia assistiva real.
- Repetir a matriz com modelos ONNX e providers CPU/GPU/NPU suportados, incluindo primeira carga, memória/VRAM, latência de quadro e qualidade por corpus revisado por humanos.
- Confirmar, em ambiente real sem segredos, que os controles de privacidade e as mensagens de recusa preservam o fallback determinístico.

Os testes automatizados e as medições já registradas estão na [matriz](../../15-matriz-de-validacao.md) e no [perfil de performance](../../auto-complite/performance.md#perfil-da-ia-local--fase-4-ctrl-lote-a44--19092026). Eles não substituem esses cenários.
