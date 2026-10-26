# SSH Tunnel Manager

Aplicação desktop Windows em C# / WPF / .NET 10 para iniciar e encerrar um túnel SSH local.

## Requisitos

- Windows 10/11 x64.
- .NET 10 SDK para compilar/publicar.
- OpenSSH Client instalado e `ssh.exe` acessível pelo PATH (ou configure o caminho completo em Settings).
- Chave privada SSH válida e acesso ao servidor.

## Abrir no Visual Studio

Abra `SshTunnelManager.csproj` no Visual Studio com suporte a .NET 10 e WPF. Compile e execute.

## Publicar como executável único

No PowerShell, dentro da pasta do projeto:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\publish.ps1
```

O arquivo gerado será `publish\win-x64\SshTunnelManager.exe`. A publicação self-contained inclui o runtime .NET, mas não inclui o cliente OpenSSH do Windows nem a chave privada.

Alternativamente, execute:

```powershell
dotnet publish .\SshTunnelManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish\win-x64
```

## Primeira execução

1. Abra a aplicação.
2. Entre em **Settings**.
3. Substitua `MEU-IP` pelo IP ou hostname real da VM.
4. Confira o usuário `ubuntu` e o caminho da chave privada. Por padrão, `ssh-key.key` é procurado na mesma pasta do executável. Também é possível escolher um arquivo ou informar caminho absoluto.
5. Confira as portas local/remota e a URL do navegador.
6. Clique em **Salvar configurações** e depois em **Conectar**.

A aplicação executa um comando equivalente a:

```powershell
ssh.exe -i "C:\caminho\ssh-key.key" -N -T -v -L 127.0.0.1:8080:127.0.0.1:8080 -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 -o ServerAliveCountMax=3 ubuntu@MEU-IP
```

A aba **Logs** mostra a saída do cliente SSH. Ao confirmar que o encaminhamento local foi iniciado, a aplicação abre a URL no navegador padrão. O botão **Desconectar** encerra o processo SSH. Fechar a janela também encerra o processo iniciado pela aplicação.

## Arquivo de configurações

As configurações são armazenadas em `%LOCALAPPDATA%\SshTunnelManager\settings.json`. A chave privada não é copiada nem armazenada pela aplicação.

## Segurança e comportamento

- Não execute como administrador sem necessidade.
- Não coloque a chave privada no Git ou em pastas compartilhadas.
- O encaminhamento escuta apenas em `127.0.0.1`, não em todas as interfaces de rede.
- A aplicação não tenta reconectar automaticamente quando o processo SSH termina; registra a saída e aguarda ação manual.
- O status representa o processo SSH e a confirmação do encaminhamento local. Ele não garante que o serviço HTTP remoto esteja saudável.
- A primeira conexão pode exigir confirmação da host key do servidor. Isso aparece nos Logs/terminal de SSH, conforme a configuração do OpenSSH; valide a fingerprint por um canal confiável antes de confiar no servidor.

## Limitações conhecidas

A confirmação de prontidão depende da mensagem de encaminhamento emitida pelo OpenSSH em modo verbose. Se uma versão/idioma do OpenSSH não emitir a mensagem esperada, o aplicativo atingirá o timeout de 25 segundos e registrará o ocorrido. O cliente SSH do Windows normalmente usa mensagens em inglês.

## Gerar o executável pelo GitHub Actions (sem instalar .NET localmente)

O workflow `.github/workflows/build-windows.yml` compila e publica o aplicativo em um runner Windows do GitHub Actions usando o SDK do .NET 10. O resultado é um executável Windows x64 autocontido (não exige instalar o runtime .NET na máquina de destino).

1. Envie o conteúdo desta pasta para um repositório GitHub. O workflow funciona tanto se esta pasta for a raiz do repositório quanto se estiver dentro de uma subpasta chamada `SshTunnelManager`.
2. Abra a aba **Actions** do repositório.
3. Selecione **Build and Publish Windows EXE** e clique em **Run workflow**. Também é executado ao enviar uma tag Git cujo nome comece com `v`, por exemplo `v1.0.0`.
4. Aguarde o job terminar e abra a execução concluída.
5. Na seção **Artifacts**, baixe `SshTunnelManager-windows-x64` e extraia `SshTunnelManager.exe`.

O artifact fica disponível por 30 dias. Este workflow publica somente o executável, não a chave SSH nem as configurações pessoais. A máquina que executa o programa ainda precisa ter o OpenSSH Client (`ssh.exe`) disponível e acesso à chave privada configurada.
