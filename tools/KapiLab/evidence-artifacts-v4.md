# Evidências de gates — contrato local v4

`kapilab-run-evidence-v4` adiciona `evidence_pointer` por métrica. `evidence_ref` continua sendo somente o caminho relativo do artefato dentro do workspace; não use fragmentos (`#...`) como seletor. `evidence_pointer` é um JSON Pointer RFC 6901 independente do caminho.

```json
{
  "schema": "kapilab-run-evidence-v4",
  "status": "completed",
  "run_id": "run-local-1",
  "qualified": true,
  "identity": {
    "build_fingerprint": "build-fingerprint",
    "package_fingerprint": "package-fingerprint",
    "backend": "Cpu",
    "provider": "local"
  },
  "metrics": [
    {
      "id": "latency-p95",
      "value": 90,
      "evidence_ref": "reports/metrics.json",
      "evidence_pointer": "/autocomplete/p95_ms",
      "evidence_sha256": "<64 caracteres hexadecimais SHA-256>",
      "qualified": true
    }
  ]
}
```

O avaliador confere que o arquivo cabe no limite por artefato e total, lê os bytes uma vez para esse vínculo, verifica `evidence_sha256` e resolve o ponteiro sobre os mesmos bytes. O alvo deve existir e ser um número JSON finito exatamente igual a `value`; objetos com propriedades duplicadas, JSON inválido, ponteiro inválido/ausente, alvo ausente, `null`, texto ou valor divergente não aprovam. O relatório local resultante é `kapilab-report-v4`; não inclui caminho nem ponteiro, somente hashes da referência e do conteúdo. `report merge` aceita v4 com as mesmas verificações de identidade e de ambos os hashes exigidas para v3.

v1/v2 continuam sem hash verificável e não aprovam. v3 mantém seu comportamento documentado em [evidence-artifacts-v3.md](evidence-artifacts-v3.md): o fragmento opcional de `evidence_ref` é apenas parte da referência hasheada e não é interpretado. Este formato v4 é uma extensão local e não declara compatibilidade com o schema externo.
