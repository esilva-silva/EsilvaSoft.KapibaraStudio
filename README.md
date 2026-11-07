# EsilvaSoft.KapibaraStudio

**MongoDB Database Management & Development Tool** — Studio desktop de código aberto em **.NET 10/Avalonia**, para **Windows e Linux**, sob licença **MIT**.

O Studio reúne conexão, exploração de bancos, Console JavaScript, consultas, edição de documentos BSON, índices, views, transferência lógica e assistência opcional por IA. O foco é gestão cotidiana de bancos e desenvolvimento.

## Documentação oficial

Comece por [docs](docs/README.md):

- [MVP](docs/MVP/README.md): funcionalidades mantidas, estado real, arquitetura, design system e guia.
- [Remover](docs/Remover/README.md): cortes executados, preservações, inventário técnico e TODOs.
- [Melhorias](docs/Melhorias/README.md): conceitos pós-MVP, sem fases ou versões comprometidas.

A nova sequência de entregas começa em **v0.10.0** e termina no aceite estável **v1.0.0**. São versões-alvo documentais, sem renumerar tags, binários ou evidências existentes. Consulte as [seis fases](docs/MVP/fases/README.md) e o [catálogo de implementação e validação](docs/MVP/02-requisitos-e-estado.md).

A documentação está consolidada em `docs`, com contratos, guia, fases e evidências suficientes para o escopo atual. O acervo anterior e seu leitor HTML foram removidos. Consulte a [baseline de validação](docs/MVP/09-baseline-de-validacao.md) para resultados registrados e pendências; a consolidação não declara novas funcionalidades ou homologações.

## Estado do produto

O projeto permanece experimental. Implementação e testes automatizados não comprovam homologação integral com MongoDB real, modelos, hardware, leitores de tela ou diálogos nativos. Evidências e lacunas estão em [validação](docs/MVP/06-validacao-e-evidencias.md); não há declaração de conclusão de revisão humana, auditoria de segurança ou estabilidade de produção por esta reorganização.

O ciclo básico já implementado é conectar → navegar → consultar → visualizar → editar → exportar. Autocomplete determinístico funciona sem modelo; autocomplete com IA é explícito por `Ctrl+;`. Agente local e GitHub Copilot têm critérios residuais próprios. Claude, Codex, APIs externas e MCP externo ficam adiados com código e disponibilidade atuais preservados.

## Executar em desenvolvimento

Requisitos: SDK fixado em [global.json](global.json), Windows/Linux e acesso a MongoDB para executar consultas. Um modelo ONNX compatível é opcional; não é necessário para explorar o Studio nem usar autocomplete determinístico.

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet run --project src/EsilvaSoft.KapibaraStudio.Desktop
```

Comece pelo [guia de uso](docs/MVP/05-guia-de-uso.md). Abrir coleção ou arquivo prepara o editor, sem executar. Consultas e escritas dependem da ação humana; confirmações e modo somente leitura continuam aplicáveis.

## Desenvolvimento e validação

Leia [AGENTS.md](AGENTS.md) antes de alterar código. Para alterações visuais, leia o [design system](docs/MVP/04-design-system.md).

```powershell
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
```

Em ambiente que bloqueia a telemetria de build Avalonia, `-p:UsedAvaloniaProducts=` permite validar sem essa tarefa externa. Não desabilita analisadores nem testes. Mantenha o mesmo backend ONNX em restore/build/test/publish; consulte [distribuição e suporte](docs/MVP/07-distribuicao-e-suporte.md).

O CI roda UnitTests e Infrastructure.Agents.Tests em Windows/Linux. IntegrationTests e suas guardas são validação manual antes de push/release. Modelos reais, benchmarks e medições não são executados pelo CI/release; os critérios estão em [validação e evidências](docs/MVP/06-validacao-e-evidencias.md).

## Dados e privacidade

O workspace usa um único proprietário LiteDB para perfis, histórico, preferências e rascunhos. Valores de ambientes existentes não constituem um cofre criptográfico. Resultados e credenciais não entram nos snapshots de sessão; texto digitado pode conter dados sensíveis e a persistência respeita os opt-outs.

IA externa requer os consentimentos e permissões aplicáveis. Ferramentas de agentes não consultam documentos nem escrevem diretamente no MongoDB, mesmo com aprovação: usam metadados e saídas de execuções humanas já capturadas. Consulte os [invariantes](docs/MVP/03-arquitetura-e-invariantes.md).

## Distribuição

Consulte [distribuição e suporte](docs/MVP/07-distribuicao-e-suporte.md) para formatos Windows/Linux, backends, integridade, atualização e limites de homologação. Instalações portáteis e pacotes gerenciados Linux têm caminhos diferentes de atualização. A documentação não publica releases nem altera sua versão.

## Apoie o projeto

Contribuições via [GitHub Sponsors](https://github.com/sponsors/esilva-silva) ajudam a sustentar desenvolvimento, testes e documentação.

## Licença

[MIT](LICENSE) © 2026 EsilvaSoft. Componentes de terceiros mantêm suas licenças e avisos: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
