# Pacotes Linux por distribuição (.deb, .rpm e AUR) — 27/10/2026

**Estado: implementado e validado em WSL2 e em contêineres (x64 real, ARM64 emulado); jobs novos do GitHub Actions não executados; nada publicado.** Decisão: [ADR-078](../10-decisoes-arquiteturais.md#adr-078--pacotes-linux-por-distribuição-e-atualização-gerenciada-27102026). Este documento é o registro vigente de formato, comandos, evidências e limites; os resultados abaixo não substituem homologação em máquinas e distribuições reais.

## Objetivo

Oferecer instalação, atualização, rollback e remoção pelo gerenciador do sistema em Debian/Ubuntu (apt), Fedora (dnf) e Arch (pacman/AUR), sem republicar .NET: os pacotes reaproveitam o tarball `linux-x64`/`linux-arm64` já gerado e validado pelo `release.yml`. O tarball portátil continua existindo e **continua autoatualizável** (ADR-038). Dados do usuário (`$XDG_DATA_HOME/EsilvaSoft/KapibaraStudio`: `workspace.db`, `workspace-log.db`) nunca são tocados por instalação, atualização, rollback ou remoção/purge.

## Formatos e nomes de arquivo

| Formato | Arquivo | Arquiteturas | Gerador |
| --- | --- | --- | --- |
| deb | `kapibarastudio_<semver>_<amd64\|arm64>.deb` | amd64, arm64 | nfpm 2.48.0 |
| rpm | `kapibarastudio-<semver>-1.<x86_64\|aarch64>.rpm` | x86_64, aarch64 | nfpm 2.48.0 |
| AUR | `kapibarastudio-bin-<semver>-aur.tar.gz` (PKGBUILD + `.SRCINFO`) | x86_64, aarch64 | `PKGBUILD.in` |

`<semver>` é a SemVer original do release (por exemplo `0.9.7-rc.1`). O til das versões de pré-release aparece somente no campo Version do pacote, nunca no nome do arquivo: o GitHub renomeia assets com `~` (precaução; a renomeação não foi confirmada empiricamente). Exemplo: o arquivo `kapibarastudio_0.9.7-rc.1_amd64.deb` tem Version `0.9.7~rc.1`.

Origem: [`packaging/linux/nfpm.yaml`](../../packaging/linux/nfpm.yaml), [`packaging/linux/aur/PKGBUILD.in`](../../packaging/linux/aur/PKGBUILD.in) e [`eng/LinuxDistroPackaging.ps1`](../../eng/LinuxDistroPackaging.ps1) (`New-KapibaraDistroPackages`, `New-KapibaraAurPackage`). O pacote AUR é `kapibarastudio-bin`: o PKGBUILD baixa o tarball do GitHub Release (SHA-256 fixado), extrai sem remover estrutura e declara `options=('!strip' '!debug')` — `strip` corromperia o bundle single-file do .NET; o executável empacotado tem o mesmo SHA-256 do tarball. O `.SRCINFO` gerado é idêntico à saída de `makepkg --printsrcinfo`.

## Layout instalado

| Caminho | Conteúdo |
| --- | --- |
| `/usr/lib/kapibarastudio/` | Conteúdo intacto do tarball mais o marcador `package-manager` (texto `apt`, `dnf` ou `pacman`) |
| `/usr/bin/kapibarastudio` | Symlink para `/usr/lib/kapibarastudio/EsilvaSoft.KapibaraStudio.Desktop` |
| `/usr/share/applications/kapibarastudio.desktop` | Entrada de menu; `StartupWMClass=EsilvaSoft.KapibaraStudio.Desktop` (medido com `xprop`: `WM_CLASS` = "kapibarastudio", "EsilvaSoft.KapibaraStudio.Desktop"; conferido de novo no Fedora) |
| `/usr/share/icons/hicolor/<16…512>x<…>/apps/kapibarastudio.png` | Ícones 16, 24, 32, 48, 64, 128, 256 e 512 |
| `/usr/share/doc/kapibarastudio/copyright` (deb) ou `/usr/share/licenses/kapibarastudio/LICENSE` (rpm, Arch) | Licença MIT; no rpm o diretório `/usr/share/licenses/kapibarastudio` pertence ao pacote e não sobra vazio após `dnf remove` |

Não há scripts `postinst`/`postrm`. O marcador `package-manager` é o que liga o modo gerenciado do updater.

## Dependências

| Item | deb | rpm | Arch |
| --- | --- | --- | --- |
| C/C++ | `libc6 (>= 2.35)`, `libgcc-s1`, `libstdc++6` | `glibc`, `libgcc`, `libstdc++` | `glibc`, `gcc-libs` |
| ICU | `libicu78 \| 76 \| 74 \| 72 \| 70` | `libicu` | `icu` |
| OpenSSL / zlib | `libssl3t64 \| libssl3`, `zlib1g` | `openssl-libs`, `libz.so.1()(64bit)` (resolvido por `zlib-ng-compat` no Fedora) | `openssl`, `zlib` |
| Fontes | `fontconfig` + `fonts-dejavu-core \| fonts-liberation2 \| fonts-noto-core` | `fontconfig`, `dejavu-sans-fonts` | `fontconfig`, `ttf-font` (virtual) |
| Ícones | `hicolor-icon-theme` | `hicolor-icon-theme` | `hicolor-icon-theme` |
| X11 | `libx11-6`, `libice6`, `libsm6`, `libxrandr2`, `libxi6`, `libxcursor1` | `libX11`, `libICE`, `libSM`, `libXrandr`, `libXi`, `libXcursor` | `libx11`, `libice`, `libsm`, `libxrandr`, `libxi`, `libxcursor` |
| Recomendados (carregados por `dlopen`, únicas faltas em `/proc/<pid>/maps`) | `libgl1`, `libdbus-1-3` | `libglvnd-glx`, `dbus-libs` | — |
| Sugeridos | `gnome-keyring` (Secret Service) | — | `gnome-keyring` (optdepends) |

Pelo menos uma fonte é obrigatória: sem nenhuma fonte instalada o Avalonia aborta com "Default font family name can't be null or empty" (achado real num Arch mínimo, corrigido com `ttf-font`). O .deb tem descrição longa (resolve o `extended-description-is-empty` do lintian).

## Versão

SemVer `X.Y.Z-pre` vira `X.Y.Z~pre` no campo Version de deb/rpm (hífens internos do sufixo viram `.`; o rpm fica `X.Y.Z~pre-1`) e `X.Y.Zpre` no Arch. Isso vale só para o campo Version, não para o nome do arquivo. O til e as letras fazem o pré-release ordenar antes do final: `dpkg --compare-versions 0.9.7~rc.1 lt 0.9.7` e `vercmp` (`1.0.0beta.1` < `1.0.0`) conferidos. Metadados `+build` são recusados.

## Instalação, atualização, rollback e remoção

| Distro | Instalar | Atualizar | Rollback | Remover |
| --- | --- | --- | --- | --- |
| Debian/Ubuntu | `sudo apt install ./kapibarastudio_<semver>_amd64.deb` | `sudo apt install ./kapibarastudio_<semver>_amd64.deb` | `sudo apt install --allow-downgrades ./<antigo>.deb` | `sudo apt remove kapibarastudio` |
| Fedora | `sudo dnf install ./kapibarastudio-<semver>-1.x86_64.rpm` | `sudo dnf upgrade ./kapibarastudio-<semver>-1.x86_64.rpm` | `sudo dnf downgrade ./<antigo>.rpm` (`sudo dnf history undo last` só funciona se o pacote anterior estiver em algum repositório) | `sudo dnf remove kapibarastudio` |
| Arch (AUR) | `tar -xzf kapibarastudio-bin-<semver>-aur.tar.gz && cd kapibarastudio-bin && makepkg -si` | o mesmo comando com o `aur.tar.gz` novo | `sudo pacman -U <pacote antigo>` | `sudo pacman -R kapibarastudio-bin` |

Em arm64 use `arm64`/`aarch64` no lugar de `amd64`/`x86_64`. Os pacotes ficam nos assets do GitHub Release; não há repositório APT/DNF. Releases já publicadas (até v0.9.5) não têm `.deb`/`.rpm`: os comandos só apontam para arquivo existente a partir do primeiro release que publicar os pacotes. Helpers AUR (`yay` e similares) só podem ser citados como opção futura depois que o projeto publicar `kapibarastudio-bin` no AUR; o nome ainda não pertence ao projeto e sugerir o helper poderia instalar um pacote de terceiros.

## Atualização no aplicativo

O `AppUpdateInstaller.DetectMode` em RID `linux-*` lê o marcador `package-manager` ao lado do executável. Valor exato `apt`, `dnf` ou `pacman` (após `Trim`) resulta em `AppUpdateAvailability.PackageManaged`, avaliado **antes** do teste de escrita para que um usuário root nunca sobrescreva arquivos do gerenciador. Marcador inválido, ilegível, diretório ou maior que 64 caracteres resulta em `ManualOnly`; Windows ignora o marcador.

Em `PackageManaged`, `CheckAsync` continua consultando o GitHub Releases (o tarball do RID segue como referência de versão), mas `DownloadAsync`, `ApplyPendingOnExit` e `CleanupAfterStart` só atuam em `Supported` e `GetStagedUpdate` retorna `null`. Nenhum `updates/pending.json` é criado. A interface mantém o botão **Atualizar**; o tooltip diz "Versão {0} disponível. Esta instalação é gerenciada pelo {1}; clique para ver como atualizar." e o clique abre o diálogo **Atualizar KapibaraStudio** com o comando (`Core/LinuxPackageUpdateGuidance`) e três ações: **Copiar comando**, **Abrir página do release** e **Fechar**. Comandos exibidos:

- apt: `sudo apt install ./kapibarastudio_<semver>_<amd64|arm64>.deb`
- dnf: `sudo dnf upgrade ./kapibarastudio-<semver>-1.<x86_64|aarch64>.rpm`
- pacman: `tar -xzf kapibarastudio-bin-<semver>-aur.tar.gz && cd kapibarastudio-bin && makepkg -si`

O comando do diálogo só aponta para um arquivo existente a partir do primeiro release que publicar os pacotes. `SLOPSTUDIO_DISABLE_UPDATES=1` continua desativando tudo. O tarball portátil e o Windows não mudam.

## O que o CI faz

Nada abaixo rodou ainda no GitHub Actions (exige push de branch e `workflow_dispatch`, ainda não autorizados). Fluxo escrito:

- `ci.yml` (Linux): além do boundary de sistema, passa a rodar `AppUpdateServiceTests`.
- `linux-packages` (`release.yml`): matriz `linux-x64`/`linux-arm64` em `ubuntu-24.04`; baixa o tarball do job `package` (artefatos `linux-packages-linux-*`); instala `rpm` e `cpio` e, com `CI=true`, ferramenta de conferência ausente é erro; nfpm fixado por SHA-256; gera .deb e .rpm e, à parte, pacotes de upgrade de teste `X.Y.(Z+1)-ci.upgrade` que nunca são publicados.
- `linux-packages-test-deb`: container `ubuntu:24.04` em x64 e arm64 (container, e não runner completo, para exercitar as dependências em sistema mínimo). `linux-packages-test-rpm`: container `fedora:44` em x64 e arm64. Ambos executam [`eng/Test-LinuxDistroPackages.sh`](../../eng/Test-LinuxDistroPackages.sh); as listas de pacotes dos containers incluem `diffutils`.
- `aur-package` e `aur-test`: gera o `aur.tar.gz`; no container `archlinux:base-devel` confere `.SRCINFO` com `makepkg --printsrcinfo`, roda `makepkg` com o tarball local e testa upgrade por `pkgrel`+1.
- `release` depende de todos os testes e anexa `.deb`, `.rpm` e `aur.tar.gz`, incluindo-os em `SHA256SUMS.txt`. `workflow_dispatch` só gera artefatos.

O script `Test-LinuxDistroPackages.sh --format deb|rpm|pacman --package <v1> [--upgrade <v2>] [--run-seconds N] [--user u]` instala; confere arquivos, symlink, marcador, `.desktop`, ícones e versão; executa o app como não-root exigindo processo vivo até o timeout (124); usa Xvfb sempre que não há `DISPLAY` (só `WAYLAND_DISPLAY` não basta: o Avalonia usa X11); roda `ldd` nas `.so` extraídas em `~/.net`; após cada execução confere `dpkg --verify` / `rpm -V` / `pacman -Qkk` (o app não altera arquivos gerenciados); compara o hash dos dados do usuário antes/depois de upgrade, rollback e remoção. O código 137 tem mensagem própria.

## Resultados de validação (27/10/2026)

Versões locais 0.9.4 e 0.9.6 nunca foram publicadas. "Ciclo completo" = instalar 0.9.4, atualizar para 0.9.6, rollback, remover, com app vivo, dados intactos e arquivos gerenciados sem alteração.

| Ambiente | Resultado | Cobertura |
| --- | --- | --- |
| Build/testes Linux (Ubuntu 26.04, WSL2, SDK 10.0.400) | PASS | restore locked; build Release 0 avisos/0 erros; UnitTests 3845 aprovados/20 ignorados/0 falhas; Agents 403/403; IntegrationTests (UnitSystemBoundary + AppUpdate + LocalWorkspacePathResolver) 68/68, com o código final |
| Build/testes Windows (host) | PASS no código final; falhas não relacionadas na rodada integral anterior | build Release 0/0; UnitTests 3845/20 ignorados/0 falhas; integração AppUpdate 47 aprovados/1 ignorado (o ignorado é Linux-only). A rodada integral de IntegrationTests (1112 aprovados/20 ignorados/4 falhas) é da revisão anterior do mesmo dia: 3 falhas de teardown LiteDB (`PENDING.JSON.tmp`) que passaram 3/3 isoladas e 1 `ClaudeCodePlanTests` bloqueado por variáveis `CLAUDE*`/`ANTHROPIC_*` herdadas da sessão (comportamento correto; passa sem elas). Empacotamento Windows sem alteração. PNGs headless `update-packagemanaged-*` inspecionados (claro/escuro) |
| Tarballs 0.9.4 e 0.9.6 | PASS | linux-x64 (78,7 MB) e linux-arm64 (75,3 MB, cross-compilado) com os passos do `release.yml`; SBOM validado (no WSL o `sbom-tool` precisou de `DOTNET_ROLL_FORWARD=Major` por faltar runtime .NET 8; runners do GitHub têm) |
| Ubuntu 26.04 x64 (.deb, apt, WSL) | PASS | ciclo completo; rollback por `apt-get install --allow-downgrades`; `desktop-file-validate` ok; `dpkg --verify` limpo. Detecção real contra o feed público: 0.9.4 mostrou o botão e o diálogo com `sudo apt install ./kapibarastudio_0.9.5_amd64.deb`; 0.9.6 não mostrou botão; nenhum `pending.json` |
| Fedora 44 x64 (.rpm, dnf5 5.4.1, WSL) | PASS | ciclo completo; rollback efetivo por `dnf downgrade` (`dnf history undo last` falha sem repositório com "no match for kapibarastudio-0.9.4-1.x86_64", esperado; o script cai para o downgrade); `rpm -V` vazio com o app rodando e após; Requires resolvem; `/proc/<pid>/maps` sem faltas além de libGL/libdbus (`dlopen`, por isso Recommends); `WM_CLASS` confere. Detecção real: botão **Atualizar** no WSLg; o clique por `xdotool` não chega ao app sob WSLg/Weston (limitação do ambiente); em Xvfb o diálogo abriu com `sudo dnf upgrade ./kapibarastudio-0.9.5-1.x86_64.rpm` |
| Arch Linux x64 (WSL, makepkg/pacman 7.1) | PASS (após correção) | `makepkg -s` com tarball local; sha256 do executável idêntico ao do tarball; primeira rodada falhou em sistema sem fontes (corrigido com `ttf-font` e `hicolor-icon-theme`); ciclo completo com rollback por `pacman -U`; `pacman -Qkk` sem alterações; namcap: apenas aviso x86_64 → `$CARCH`. Detecção real: diálogo para 0.9.5 (na época com o comando `yay`, substituído depois pelo `tar … makepkg -si`) |
| ARM64 emulado (qemu-user, podman, imagens oficiais arm64; **não é hardware real**) | PASS | `ubuntu:24.04` (.deb) e `fedora:44` (.rpm): ciclo completo, app vivo 120 s sob Xvfb nas 3 execuções (~7 min por ciclo emulado); ELF AArch64 conferido; dependências resolvem nos repositórios arm64. `ubuntu:26.04` arm64: instalação e remoção OK (libicu78, libssl3t64) |
| Reteste final com os pacotes finais, contêineres mínimos sem nenhuma fonte (`fc-list` = 0) | PASS | script completo (0.9.4 → 0.9.6, rollback, remoção, arquivos gerenciados) em x64 `ubuntu:24.04`, `ubuntu:26.04`, `fedora:44` (sem resíduo em `/usr/share/licenses`) e `archlinux:base-devel` (`.SRCINFO` idêntico ao do makepkg; `makepkg` + `pacman -U` resolvendo `ttf-font`); ARM64 emulado `ubuntu:24.04` e `fedora:44` |
| Arch aarch64 | Só estático | conferência de source/sha256 do PKGBUILD; sem ambiente Arch ARM |
| Jobs novos no GitHub Actions | Não executado | exige push de branch e `workflow_dispatch`; disponibilidade de `ubuntu-24.04-arm`, actions JavaScript em containers fedora/arch/ubuntu no runner ARM e a renomeação de `~` pelo GitHub não confirmadas |

Ocorrências não diagnosticadas: uma execução isolada em Xvfb sem gerenciador de janelas morreu após ~75 s com "X connection to :99 broken" e não se repetiu; uma execução ARM64 emulada recebeu SIGTERM (143) externo, também não reproduzido. Captura de tela no WSLg só funciona por janela (`import -window <id>`); `-window root` falha com `BadMatch`.

Verificadores estáticos: lintian (.deb) mantém E `embedded-library` (single-file, esperado) e `no-changelog` (esperado), mais W `absolute-symlink-in-top-level-folder` e `no-manual-page`; `extended-description-is-empty` foi corrigido. rpmlint 2.8 reporta `zero-perms` (ver limitações), `no-manual-page-for-binary`, `no-documentation`, `no-changelogname-tag` e `explicit-lib-dependency`, aceitos.

## Limitações

- WSL2 e contêineres não são uma distribuição gráfica real: o resultado prova instalação, execução sob Xvfb, dependências e fluxo do gerenciador, não a experiência em desktop (menu, ícone na barra de tarefas, agrupamento de janelas). O clique no botão sob WSLg não foi exercitado (limitação do `xdotool` no ambiente).
- ARM64 só em emulação (qemu-user) e cross-compilação, nunca em hardware real; Arch ARM não testado.
- Wayland nativo não testado: o Avalonia usa X11/XWayland.
- Clipboard, diálogos nativos de arquivo e leitor de tela não validados nesses pacotes. Execução sem `libGL` (recomendada, não obrigatória) não testada.
- O nfpm 2.48 grava o symlink `/usr/bin/kapibarastudio` do rpm com modo `l---------` (rpmlint `zero-perms`). O Linux ignora o modo de symlink e o comando funciona.
- O diretório de extração `~/.net` (bundle single-file) acumula uma pasta por versão; remover o pacote não o limpa.
- O SHA-256 do mesmo release detecta corrupção, não comprometimento da conta ou do pipeline. Não há assinatura GPG nem repositório APT/DNF assinado; o AUR depende do hash do PKGBUILD.
- AUR não publicado: `kapibarastudio-bin` existe apenas como `aur.tar.gz` gerado, e o nome ainda não pertence ao projeto no AUR.
- Distribuições além de Ubuntu 24.04/26.04, Fedora 44 e Arch rolling não foram exercitadas; as faixas de ICU/OpenSSL do .deb não foram testadas em versões anteriores às listadas.

## O que falta para publicar

1. Rodar `release.yml` por `workflow_dispatch` numa branch e confirmar a disponibilidade do runner `ubuntu-24.04-arm`, o funcionamento de actions JavaScript nos containers (inclusive no runner ARM), a renomeação de `~` pelo GitHub e o verde dos jobs novos.
2. Criar a tag `v*`; o `release` anexa os pacotes e os hashes.
3. Publicar `kapibarastudio-bin` no AUR com a chave SSH do mantenedor (ação manual fora do pipeline).
4. Opcional: repositório APT/DNF assinado, para atualização sem baixar arquivo e com autenticidade verificável.
