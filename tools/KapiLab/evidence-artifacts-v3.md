# Evidências locais com integridade de artefatos

`report evaluate` aceita `kapilab-run-evidence-v3`, com os mesmos campos de identidade/qualificação do v2, `status` obrigatório e `evidence_sha256` hexadecimal SHA-256 por métrica. A referência `evidence_ref` deve apontar a arquivo relativo ao workspace. Um fragmento opcional, como `#p95`, é preservado na identidade da referência, mas não é interpretado como seletor de valor.

```json
{
  "schema": "kapilab-run-evidence-v3",
  "status": "completed",
  "run_id": "rodada-local",
  "qualified": true,
  "identity": {
    "build_fingerprint": "build-observada",
    "package_fingerprint": "pacote-observado",
    "backend": "Cpu",
    "provider": "local"
  },
  "metrics": [{
    "id": "p95_ms",
    "value": 90,
    "evidence_ref": "reports/lab/metrics.json#p95",
    "evidence_sha256": "SHA-256 hexadecimal de 64 caracteres dos bytes de metrics.json",
    "qualified": true
  }]
}
```

O hash ilustrativo deve ser substituído pelo SHA-256 real do arquivo. A avaliação abre o arquivo em modo de leitura, recusa referências externas/traversal que saia da raiz, links/junctions, conjunto cego e colisão com a saída do relatório. Hash divergente ou arquivo ausente gera `evidence-integrity-failed`, relatório incompleto e código 6. Ausência de hash ou de workspace de verificação gera `unverified-evidence`, também sem aprovação. Referência insegura é recusada com código 8; hash inválido ou orçamento excedido usa código 3. Limites: 64 MiB por leitura de artefato e 256 MiB acumulados na avaliação, inclusive leituras repetidas.

A saída é `kapilab-report-v3`: cada gate avaliado com evidência íntegra registra `evidenceArtifactSha256` dos bytes, além de `evidenceReferenceSha256` da referência, sem publicar seu caminho. Entradas v1/v2 continuam legíveis e preservam estados skipped/inconclusive, mas seus valores sem hash não aprovam gates. Para migrar, gerar evidência v3 com hashes e reavaliar a rodada. O merge admite relatórios antigos incompletos; recusa gates passed/failed de report v2 e exige os dois hashes em report v3. O envelope consolidado continua `kapilab-merged-report-v1`.

Este recorte prova existência/integridade dos bytes observados durante a avaliação. Não valida semanticamente a métrica dentro do arquivo, fragmentos, ownership, identidade declarada pelo produtor ou equivalência com schemas externos. O merge preserva a evidência registrada na avaliação; não reabre artefatos, e os relatórios não possuem assinatura. Alteração posterior do arquivo requer nova avaliação. F7B-10/22 permanecem parciais.
