# Explorer e revisão do updater — 03/11/2026

## Abrir coleção

Duplo clique esquerdo na linha de uma coleção cria e ativa uma **nova aba Console**, capturando perfil, banco e coleção. O texto inicial é `db.getCollection("nome").find({}).limit(100)`, com o nome serializado como literal JSON. Uma aba existente da mesma coleção nunca é sobrescrita nem reutilizada por essa ação. Enter e **Abrir documentos** usam a mesma abertura de rascunho novo.

Clique simples apenas navega. Clique direito abre o menu contextual; duplo clique no expansor continua expandindo/recolhendo. Não há execução de consulta ao abrir, nem redirecionamento de abas quando a seleção do explorer muda. A execução permanece explícita pelo usuário.

Os eventos de ponteiro e Enter são tratados na fase Tunnel da árvore: o TreeView pode absorver os eventos de bolha para seleção/expansão. O ponteiro resolve a linha efetivamente clicada, sem depender de uma seleção anterior. Não há mudança de layout, temas ou atalhos globais.

## Revisão do updater

A consulta pública à [API do v0.9.8](https://api.github.com/repos/esilva-silva/EsilvaSoft.KapibaraStudio/releases/tags/v0.9.8), em 03/11/2026, confirmou release estável, quatro arquivos portáteis (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`), pacotes deb/rpm/AUR e `SHA256SUMS.txt`. Todos publicavam `digest` SHA-256. O snapshot dos campos consumidos pela aplicação está em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/Fixtures/app-update-v0.9.8.json`; os testes regulares usam HTTP falso, sem rede.

O seletor já reconhecia os nomes do v0.9.8 e preserva o `browser_download_url` publicado. Uma instalação portátil escolhe ZIP/tarball do próprio RID, sem confundir com deb/rpm/AUR. Instalações Linux gerenciadas preservam o fluxo do gerenciador (ADR-078); não recebem substituição automática de arquivos.

**Correção:** `HttpClient.Timeout` é infinito para permitir downloads grandes, mas faltava limitar a espera por checksums e pelos cabeçalhos/redirecionamentos do pacote. Agora o checksum tem prazo de 15 segundos e a conexão/cabeçalhos do pacote têm prazo de 60 segundos. Durante a leitura, o prazo de 60 segundos é renovado por bloco recebido. Timeout resulta em falha visível e descarte do staging; cancelamento do usuário conserva sua semântica. SHA inválido, pacote inválido, falha de disco e cancelamento não publicam atualização pendente.

## Evidências e limites

- Restore locked e build serial com `-p:UsedAvaloniaProducts=` passaram sem avisos/erros.
- UnitTests: 3853 aprovados, 20 ignorados. Novos casos cobrem rascunhos independentes/recuperação e timeout/cancelamento/checksum do download.
- Infrastructure.Agents.Tests: 403 aprovados.
- Headless testa clique simples, duplo clique esquerdo repetido, clique direito e Enter; preserva a edição anterior e verifica ausência de execução. Os 18 PNGs reais foram inspecionados (dois temas, três tamanhos e três escalas), com abas, script, ação Executar e resultados vazios preservados. Arquivos `collection-doubleclick-*.png` em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/bin/Debug/net10.0/ui-evidence/render-343317ad536a4eeab6f7532ec9469160/`. O erro de detalhes visível nas capturas é a resposta do double para metadados não implementados, sem consulta de documentos.
- Integração filtrada: 64 aprovados, 1 ignorado por exigir Linux. Inclui seleção dos quatro RIDs com o snapshot real, staging, substituição/recuperação em pastas sintéticas e guardas `UnitSystemBoundaryIntegrationTests`, inclusive `RealModelHubAccessIsConfinedToExplicitLocalModelTests`.
- Teste manual `Published098WindowsPackageDownloadsVerifiesAndStagesWithoutReplacingInstallation`, marcado `[Explicit]`/`AppUpdateLive`, passou: o serviço de produção baixou **84.466.821 bytes**, verificou SHA-256 **`2dc0d3a6aafde3479a67f20a56e91458b4c3cd726eeba1a05f5b8d74e1b0753b`**, extraiu o executável e gravou pendência somente em pasta temporária; o arquivo sentinela da instalação sintética permaneceu intacto. O teste não aplica nem inicia o executável baixado.
- TRX em `tests/<projeto>/TestResults/explorer-updater.trx`; download real em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/TestResults/updater-v098-live.trx`.

Não houve atualização da instalação real, execução do binário baixado, download real ARM64/Linux ou homologação nativa do duplo clique/leitor de tela. A substituição do executável é coberta por fixtures sintéticas; o teste Linux de permissões exige Linux. A suíte completa IntegrationTests não foi executada.
