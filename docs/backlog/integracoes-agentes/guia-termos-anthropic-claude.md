# Guia de validação dos termos Anthropic — Claude Code no Agente IA

**Destino de planejamento — 03/10/2026:** backlog sem fase ou versão comprometida, conforme [bkl-06](../bkl-06-integracoes-agentes.md). As evidências, implementações, limitações e gates abaixo são preservados; esta integração não compõe o aceite da Fase 5. Novos trabalhos dependem de repriorização explícita.

**Revisão documental:** 02/10/2026  
**Estado:** análise técnica preliminar; liberação comercial não aprovada.  
**Escopo:** Claude Code oficial iniciado pelo painel Agente IA do KapibaraStudio. O provider HTTP Anthropic legado não é registrado no catálogo Desktop e não aparece como modalidade no painel. Este guia não é parecer jurídico nem autorização da Anthropic.

## Conclusão executiva

A documentação oficial consultada permite que uma pessoa entre com sua própria assinatura no binário Claude Code oficial e pague diretamente pelo uso, inclusive quando o binário é oferecido por uma plataforma. Essa possibilidade está condicionada aos requisitos de oferta do Claude Code em produtos: salvo acordo diferente, aceitar os Commercial Terms, executar o binário sem modificações e sem remover, desativar ou restringir métodos de autenticação nativos, além de não pagar, revender ou intermediar o uso em nome dos usuários. A página diz “preinstalling or running Claude Code in your products or services”; portanto, baixar e instalar a CLI por conta própria não parece, por si só, dispensar o acordo quando o KapibaraStudio a inicia e a integra ao painel. A Anthropic deve confirmar o enquadramento exato.

A mesma página oficial instrui quem está construindo produto, aplicação ou ferramenta para terceiros a usar autenticação por API key e proíbe login Claude.ai no próprio aplicativo, coleta/intermediação de credenciais ou tráfego de planos Free/Pro/Max em nome dos usuários. Ela ressalva que o usuário pode entrar no binário Claude Code não modificado dentro de um produto, nos termos acima. A documentação não resolve publicamente todos os detalhes desta arquitetura desktop específica.

### CLI oficial com API key não é integração direta pela API

Há duas situações diferentes. Se o KapibaraStudio inicia o binário oficial Claude Code e o próprio usuário configura nele sua API key do Claude Console, o uso e a cobrança seguem a credencial e o contrato do titular com Anthropic; isso não transforma o painel em um cliente HTTP da API. Ainda assim, por o produto executar Claude Code como parte de sua função, a exigência publicada para oferecer ou executar Claude Code em um produto continua aplicável: salvo acordo diferente, Commercial Terms, binário sem modificações, métodos nativos preservados e cobrança direta ao usuário. A autenticação por API key não dispensa esse enquadramento comercial.

Se o KapibaraStudio fizer chamadas diretamente à Anthropic API usando uma API key (sem passar pelo Claude Code), essa será uma integração de produto com a API e deverá observar os termos comerciais/API próprios da conta que fornece a chave. A página de conformidade recomenda API key para desenvolvedores que constroem produtos e proíbe intermediar uso de assinatura Free/Pro/Max. Isso é distinto da autorização para incluir/executar o Claude Code oficial; aceitar os termos aplicáveis à API, por si só, não confirma autorização para executar a CLI no produto. A situação atual do painel é a primeira, exclusivamente CLI; a modalidade HTTP Anthropic não está registrada no catálogo Desktop.

Fontes: [Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance) (termos aplicáveis a Claude Code, execução em produtos, uso de API e limites de autenticação) e [Authentication](https://code.claude.com/docs/en/authentication) (API key do Console e demais métodos nativos suportados).

Por isso, a implementação atual não deve ser anunciada, publicada ou habilitada para distribuição comercial como integração Claude Code no painel até obter resposta escrita da Anthropic sobre o caso concreto, confirmação jurídica dos termos que KapibaraStudio deve aceitar e verificação técnica de que a integração não restringe métodos de autenticação nativos. A documentação pública, por si só, não confirma conformidade.

Como controle temporário, o Desktop em configuração `Release` não registra Claude Code no catálogo, não compõe handlers de conta da CLI e não empacota o proxy MCP do provider; o modo de desenvolvimento continua disponível para validação local. A implementação do adapter ainda é compilada na dependência compartilhada `Infrastructure.Agents`, mas não há caminho de composição para iniciá-la no Desktop Release. O release workflow força `EnableClaudeCodeIntegration=false`, e a validação estrutural do pacote rejeita a presença do diretório `mcp/`.

## Fontes oficiais consultadas

Consulta em 02/10/2026. Revalidar antes de cada lançamento porque termos, políticas e páginas de suporte podem mudar.

1. [Claude Code — Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance): cláusulas específicas para produtos, binário não modificado, métodos de autenticação, cobrança direta, autenticação de desenvolvedores e marcas.
2. [Anthropic Help Center — Log in to your Claude account](https://support.claude.com/en/articles/13189465-log-in-to-your-claude-account): uso de terceiros, preferência por API key, créditos de uso e orientação para desenvolvedores.
3. [Anthropic Consumer Terms](https://www.anthropic.com/legal/consumer-terms): conta individual, automação, entradas e ações, responsabilidades e pagamentos.
4. [Anthropic Commercial Terms](https://www.anthropic.com/legal/commercial-terms): termos comerciais, uso, políticas incorporadas, taxas, suspensão e obrigações contratuais.
5. [Claude Code — Set up Claude Code](https://docs.anthropic.com/en/docs/claude-code/getting-started) e [uso com Pro/Max](https://support.claude.com/en/articles/11145838-use-claude-code-with-your-pro-or-max-plan): instalação, autenticação suportada, limites e separação dos créditos de API.

## Requisitos identificados e leitura para este produto

| Requisito público | Evidência no desenho atual | Estado / ação |
| --- | --- | --- |
| Executar Claude Code dentro de um produto requer acordo com os Commercial Terms, salvo acordo diferente | O KapibaraStudio inicia a CLI oficial a partir do painel; o ADR-057 descreve essa integração. O usuário instalar a CLI pela fonte oficial não muda que o produto a executa/invoca no painel | **Pendente bloqueante.** Jurídico deve identificar a entidade que aceita os termos e confirmar escopo/território. Consultar Anthropic Sales para aprovação escrita do caso. |
| Binário deve ser publicado pela Anthropic e não modificado | A integração chama a CLI instalada; não há patch declarado ao binário | **Parcialmente verificado.** Registrar origem, versão, assinatura/hash, processo de instalação/atualização e provar que releases não empacotam binário alterado. Argumentos de execução e configuração MCP também devem ser apresentados à Anthropic para confirmar que não constituem modificação/restrição vedada. |
| Não remover, desativar ou restringir autenticações nativas, incluindo Claude account e API key própria | No painel, o provider inicia a CLI oficial e delega autenticação aos métodos nativos; não há provider HTTP Anthropic direto no catálogo Desktop. O host bloqueia nomes de variáveis que alteram destino, transporte ou sessão hospedeira, sem ler seus valores | **Pendente bloqueante.** O checkout não exige assinatura nem filtra o método nativo efetivo, mas o efeito contratual dos bloqueios de destino/transporte/configuração precisa de confirmação. Revisar argumentos e UX com a Anthropic sem ler segredos; não criar login próprio nem capturar credenciais. |
| Cada usuário deve usar suas próprias credenciais e ser faturado diretamente | Login/status/logout delegados à CLI, sem leitura de credenciais ou tokens; sem fallback silencioso para API. A CLI determina o método de cobrança | **Evidência favorável no desenho, não suficiente para aceite.** Confirmar com Anthropic que o fluxo executado pelo painel qualifica como login do usuário no binário oficial. Informar que custo, limites e créditos variam conforme o método usado e comprovar que permanecem sob controle/faturamento do usuário. |
| O produto não pode pagar, revender ou intermediar Claude Code em nome dos usuários | KapibaraStudio não declara comprar ou revender inferência; assinatura é pessoal e API é modalidade distinta | **Pendente de confirmação comercial.** Validar monetização do KapibaraStudio, instalação/distribuição da CLI e eventual oferta a clientes/empresas. |
| Desenvolvedores de produto devem usar API key; não oferecer login Claude.ai no próprio app nem rotear tráfego de planos pessoais em nome do usuário | O login ocorre no executável oficial, não em formulário/WebView próprio; o painel chama a CLI para cada turno | **Ambiguidade central.** Obter resposta escrita sobre esse painel desktop, que executa Claude Code e transporta mensagens/contexto por processo filho, e confirmar se é a exceção documentada ou um produto de terceiro que deve usar API. |
| Não coletar, armazenar ou intermediar credenciais/tokens | Código e ADR proíbem leitura de arquivos de credenciais, tokens, endpoints privados e fallback | **Manter e provar.** Inspecionar logs, dumps, telemetria, diagnósticos, ambiente do processo, crash reports e histórico local para garantir que não registram credenciais/tokens. Não capturar dados de autenticação para produzir essa prova. |
| Uso respeita Consumer/Commercial Terms, Usage Policy e países/regiões suportados | Consentimento de contexto e permissões limita alguns dados; o usuário escolhe a conta oficial | **Pendente de produto e operação.** Determinar plano alvo (Free/Pro/Max/Team/Enterprise), público e territórios; revisar Usage Policy, exportação, dados Mongo/workspace, política de privacidade e suporte a restrições organizacionais. |
| Uso de nomes e marcas não pode sugerir que Anthropic fez, endossa ou tem parceria com o produto | Interface usa rótulos “Claude” e “Claude Code” para descrever o modo | **Revisar marca e apresentação.** Manter descrição factual em texto; não usar marca/logo como nome do produto, feature ou empresa e não insinuar endosso/parceria. Validar ícones, página de download, instalador, release notes e marketing. |

## O que falta fazer

### 1. Confirmar o caso com a Anthropic

Preparar um pacote técnico e solicitar resposta escrita de Sales/representante autorizado. Informar explicitamente:

- KapibaraStudio é uma IDE desktop distribuída a usuários finais, com painel de agente que inicia a CLI oficial localmente.
- Quem instala a CLI e como ela é atualizada; versões suportadas; se o produto a redistribui ou apenas detecta instalação do usuário.
- Que o app passa prompts, contexto selecionado pelo usuário, snapshots do workspace e tools locais via MCP; que a execução e cobrança direta são por conta do usuário e variam conforme o método de autenticação escolhido na CLI.
- Como `auth status`, login, logout, allowlist de modalidade e ambiente filtrado se comportam; que o produto tem modo de API separado.
- Como a CLI é configurada e quais argumentos são passados, inclusive configuração MCP, prompt adicional, permissões e bloqueios de destino/transporte.
- Que o fluxo não lê/apaga credenciais Claude legadas no cofre e não migra automaticamente conversas antigas; confirmar se há exigências de aviso, retenção ou transição de dados.
- Modelo de distribuição/licença do KapibaraStudio, monetização, mercados/países e contas Claude suportadas.

Perguntas que precisam de resposta explícita:

1. O uso descrito se enquadra na exceção de oferecer/executar o binário Claude Code não modificado, preservando seus métodos nativos, com login e cobrança do próprio usuário?
2. A integração via painel embutido com transporte do prompt/contexto ao processo Claude Code é permitida para Claude Free/Pro/Max/Team/Enterprise? Alguma categoria de plano fica excluída?
3. O produto precisa aceitar os Commercial Terms antes de habilitar/distribuir esta função? Quem deve ser a parte contratante e há autorização ou adendo necessário?
4. O painel que inicia Claude Code oficial, deixa seus métodos nativos disponíveis e informa que a cobrança varia pelo método efetivo satisfaz a exigência de não restringir autenticação nativa, sem expor uma rota HTTP Anthropic direta no painel?
5. Quais métodos de autenticação o host deve deixar disponíveis? Os bloqueios de destino, transporte e sessão hospedeira são permitidos? Há requisito para instalação, atualização, argumentos, MCP, nomes/logos ou avisos sobre cobrança?
6. A Anthropic considera que o uso local/individual dentro de uma IDE de terceiros constitui tráfego roteado por ferramenta de terceiro ou uso ordinário do Claude Code?

Guardar a resposta, termos aceitos, data, entidade contratante, escopo, país e condições no pacote de release. E-mail comercial informal não substitui revisão dos termos contratuais.

### 2. Revisão jurídica e comercial

- Obter parecer do responsável jurídico sobre Consumer Terms, Commercial Terms, Usage Policy, privacidade, proteção de dados, direitos sobre conteúdo, exportação e jurisdições de distribuição.
- Determinar formalmente se a integração pode ser oferecida sob licença MIT do KapibaraStudio, inclusive em distribuição comercial/empresarial; separar a licença do produto dos termos Anthropic e da licença da CLI.
- Confirmar se é necessária conta organizacional, contrato de cliente, DPA ou condição de suporte. Não presumir que a assinatura pessoal equivale a contrato comercial do produto.
- Registrar responsáveis por aceitação de termos e procedimento para atualizações/revogação da autorização.

### 3. Evidência técnica do cumprimento

- Inventariar launcher, allowlists, remoção de variáveis, opções CLI, configuração MCP e seleção de provider. Verificar se alguma parte remove ou impede método nativo do Claude Code.
- Verificar origem oficial, integridade e versão mínima/máxima da CLI nas plataformas suportadas; provar que nenhum patch ao executável é distribuído.
- Confirmar que login/logout e telas de autenticação permanecem dentro do fluxo oficial e que nenhuma telemetria/log/diagnóstico persiste credenciais, tokens ou texto bruto de autenticação.
- Provar por teste que indisponibilidade, limite, expiração ou erro não troca modalidade, plano, billing nem provider automaticamente; decisões de créditos/API permanecem explícitas no fluxo oficial.
- Validar como ações do MCP e dados do produto são expostos à CLI, quais dados deixam a máquina, duração e retenção local/remota e como opt-outs afetam somente os stores que prometem controlar.
- Manter o aviso claro de que dados escolhidos são enviados à Anthropic, que método efetivo determina cobrança/limites e que histórico local não apaga transcrições oficiais da CLI. Informar que conversas antigas não migram automaticamente e que credenciais legadas do cofre não são acessadas nem removidas.

### 4. Revisão de UX, marca e distribuição

- Revisar nomes “Claude”/“Claude Code”, logos, ícones, instalador, site, screenshots e notas de release contra as Trademark Guidelines. Não sugerir parceria ou aprovação sem autorização escrita.
- Explicar billing direto, cotas, eventual uso de créditos, armazenamento/transcrição e envio de contexto antes do primeiro envio; não prometer que “assinatura cobre” qualquer atividade além do que a Anthropic confirma.
- Definir mercados suportados e restringir oferta quando a Anthropic não suportar o território ou organização.
- Criar processo para suspender a função em caso de mudança dos termos, notificação de enforcement ou retirada da autorização, sem apagar dados do usuário nem mudar silenciosamente para API.

## Opções de decisão após a resposta

1. **Anthropic confirma por escrito a arquitetura atual:** registrar condições, implementar requisitos faltantes e liberar somente após revisão jurídica e gates técnicos.
2. **Anthropic exige API para integração em produto:** manter assinatura desativada para o painel distribuído e avaliar a modalidade Claude API explícita, com credencial/billing do usuário e termos próprios. Isso é decisão de produto separada; não introduzir fallback silencioso.
3. **Anthropic exige mudança no fluxo ou contrato adicional:** implementar apenas a arquitetura autorizada e revisar permissões, UX, distribuição e modelo de suporte antes de habilitar.
4. **Sem resposta ou resposta ambígua:** considerar o gate aberto; não anunciar nem liberar comercialmente a integração Claude Code no painel. Manter o estado bloqueado até resposta oficial/documentação que resolva a ambiguidade.

## Critério para fechar o gate

- [ ] Resposta escrita da Anthropic cobrindo arquitetura exata, planos, fluxo de autenticação, processamento local/MCP e distribuição.
- [ ] Commercial Terms/adição assinados pela entidade correta ou confirmação escrita de que não se aplicam ao cenário.
- [ ] Parecer jurídico/comercial documentado, com territórios, planos e limites de uso.
- [ ] Auditoria técnica comprova binário oficial íntegro, nenhum método de autenticação nativo restringido, nenhuma credencial/token coletado e cobrança direta do usuário.
- [ ] UX, privacidade, retenção, créditos, marca e suporte refletem os termos confirmados.
- [ ] Testes de regressão de billing/modo sem fallback; evidência de runtime oficial e política de desligamento/atualização.
- [ ] README da Fase 5, ADR-057, meta Claude, guia e material de release registram a mesma decisão e sua data de revisão.

Até todos os itens aplicáveis serem aprovados, o estado permanece **não homologado para distribuição comercial da integração Claude Code no painel**.
