# Release v0.8.0 — arquivos de texto e workspace local

**Arquivada em 22/09/2026 por escopo funcional.** A homologação nativa de diálogos, lixeira, permissões e acessibilidade não está concluída; as pendências estão em [pendencias-de-homologacao.md](pendencias-de-homologacao.md) e continuam abertas na [Fase 10 / v0.14.0](../../phases/phase-10-v0.14.0/README.md).

Esta pasta corresponde à [Fase 4](../../phases/phase-04-v0.8.0/README.md) do [roadmap](../../09-plano-de-implementacao.md).

## Objetivo entregue

Arquivos são abertos no editor como texto simples, sem executar o conteúdo. A mesma aba salva na origem ou em novo destino e um workspace local opera uma única raiz de arquivos com limites explícitos de caminho e recuperação de sessão.

## Requisitos concluídos e aceitos

| IDs | Recorte concluído | Evidência |
| --- | --- | --- |
| EDT-04 (recorte de texto) | Abrir, salvar e salvar como mantêm a aba; texto UTF-8, UTF-16 e UTF-32, BOM, Unicode e quebras de linha são preservados | `LocalScriptFileService`, `WorkspaceTabViewModel`; `TextFilePersistenceTests.ExistingBytesRoundTripIncludingBomUnicodeAndMixedNewlines`, `TabSavePreservesEncodingAndUndoClearsDirty` |
| EDT-04 (segurança de escrita) | Arquivo binário/malformado ou grande demais é recusado; salvamento é temporário no mesmo diretório, detecta alteração externa e não trunca o destino em cancelamento/falha | `LocalScriptFileService.SaveAsync`; `TextFilePersistenceTests.RejectsMalformedOrBinaryInput`, `SameLengthAndTimestampExternalEditStillConflictsAndKeepsDisk`, `ConcurrentSavesFromSameRevisionCannotBothOverwrite`, `CancelledSaveDoesNotTruncateOrLeaveTemporaryFiles` e `FailedReplacementLeavesOriginalAndRemovesTemporaryFile` |
| UX-01 | Rascunho recupera codificação, BOM, revisão e estado alterado sem reabrir o disco; sessão v1 migra aditivamente e estado ilegível não é sobrescrito | `WorkspaceDraft`, `LiteDbConnectionProfileRepository.WorkspaceSession`; `TextFilePersistenceTests.TabRestoresEncodingDirtyBaselineAndRevisionWithoutReplacingBufferFromDisk`, `VersionOneSessionMigratesWithoutLosingDraftsOrProfiles` e `UnknownVersionOrInvalidEncodingCannotBeOverwritten` |
| CON-10 | Uma raiz por vez; árvore ordena diretórios antes de arquivos, carrega sob demanda e não deixa enumeração antiga substituir a raiz atual | `WorkspaceViewModel.WorkspaceFiles`, `LocalWorkspaceFileService`; `WorkspaceFileNavigationTests.OldEnumerationCannotReplaceNewRootEvenWhenServiceIgnoresCancellation` |
| CON-10 (operações) | Criar, renomear e enviar à lixeira recusam colisões, links simbólicos e caminhos fora da raiz; a raiz não pode ser alterada | `LocalWorkspaceFileService`; `WorkspaceFileNavigationTests.CreateAndRenameFolderPreservesOpenTextAndDirtyState` e `SuccessfulTrashDetachesDescendantTabsWithoutDiscardingBuffers` |
| UX-01 (navegação) | Falha ao abrir não cria ou ativa aba vazia; arquivo removido preserva o buffer como documento sem caminho para salvar em outro destino | `WorkspaceViewModel.OpenTextFileAsync` e `DeleteWorkspaceNodeAsync`; `WorkspaceFileNavigationTests.FailedOpenDoesNotAddOrActivateAnEmptyTab` e `SuccessfulTrashDetachesDescendantTabsWithoutDiscardingBuffers` |

Em 22/09/2026, os filtros automatizados para `TextFilePersistenceTests`, `WorkspaceFileNavigationTests` e `LocalScriptFileServiceTests` executaram **34 testes aprovados, 0 falhas** em `net10.0`.

## Documentos desta release

- [Pendências de homologação](pendencias-de-homologacao.md)
- [Matriz de validação](../../15-matriz-de-validacao.md)
- [Guia de uso](../../14-guia-de-uso.md#arquivos-de-texto-e-workspace-local--fase-4-disponível)

## O que esta release não afirma

Não afirma homologação dos diálogos nativos de arquivo/pasta, lixeira ou permissões em Windows e Linux, acessibilidade por leitor de tela, múltiplas raízes, Git, sincronização remota, monitoramento contínuo, importação de dados ou execução de texto aberto. Nenhuma seleção no workspace executa conteúdo, consulta ou script.
