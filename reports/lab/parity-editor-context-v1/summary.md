# Paridade editor-context-v1

Registros: 2.000 (1.300 reais fim/completion + 700 sintéticos; seed 20261010).
Divergências de render: 0; divergências de tokens/contrato: 0; código diff: 0.

Chat/correction (700) excluídos: a referência usa LocalModelAiChatService/AiEditorContext; local-agent-fim-v0 captura LocalAgentProvider, outro contrato. Não é paridade de chat nem aceite de 2.000 registros reais.

Mapeamento: prefix/suffix preservados; context → editor; context.language → language; CRLF fixo no cabeçalho (367 contextos originalmente LF rematerializados); 117 contextos ausentes recebem cabeçalho padrão da IDE; prefix/suffix não são normalizados; samples statement separado no caret UTF-16. AutoTokenizer local + prompt_text/prompt_ids/QwenFim da referência externa. Orçamento solicitado e esperado 2048, completion 32; diff verifica também orçamento efetivo.

IDE: 0dbb4ddae86d781f9d352db16a8d842aac116fa7; dirty: True; GenAI: 0.15.2.

- Corpus: SHA-256 `9c5e6c8f928dd7ab4e95aa4eb10d013511ecc7420ff4f12c22c890e2f92a02fa`
- Referência Python: SHA-256 `5638f9dc0b5da50b2e706de7b8d10653899fda09aa344976d9a420675a2d3332`
- Tokenizer HF: SHA-256 `9c5ae00e602b8860cbd784ba82a8aa14e8feecec692e7076590d014d7b7fdafa`
- Tokenizer ONNX: SHA-256 `9c5ae00e602b8860cbd784ba82a8aa14e8feecec692e7076590d014d7b7fdafa`
- GenAI config: SHA-256 `098f6a6f960a801ddf2840c0049300a92ae5be513e4d60264bcd43a180f8dc64`
- diff.json: SHA-256 `38bbf274a421d86af190e01231aa7868f1752b803dd98a5ff088aff589145862`
- render-ide.jsonl: SHA-256 `bedfda4f340110abf1c677b8ddc0363780e54dcacd2bccb3b24759777b185c24`
- render-py.jsonl: SHA-256 `bedfda4f340110abf1c677b8ddc0363780e54dcacd2bccb3b24759777b185c24`
- tokens-ide.jsonl: SHA-256 `60288d0fc02d3e2285251ede8bb61c47f7ac71273fd1bcfe4438cb4f093d7697`
- tokens-py.jsonl: SHA-256 `08432946097787118733c38623282e182e889d5dbb6857c80a1538825fccca92`
