# Lembretes

Janela de lembretes flutuante para Windows (C# / WPF, .NET 10).

- Fica **sempre no topo** (botão de alfinete liga/desliga), aparece na **barra de tarefas** e na **bandeja**.
- Aviso no canto inferior direito com som quando o horário chega: **Concluído** ou **Adiar 5 min / 15 min / 1 h**.
- **Modo compacto**: duplo clique no cabeçalho (ou ⌃) deixa só a barra com o próximo lembrete.
- Transparência ajustável, “Iniciar com o Windows”, posição/tamanho lembrados.
- Dados em `%APPDATA%\Lembretes\lembretes.json`.

## Instalar

1. Baixe o `Lembretes.exe` da [última release](../../releases/latest)
   (precisa do [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)).
2. Coloque numa pasta fixa, ex.: `%LOCALAPPDATA%\Programs\Lembretes\`, e abra.
3. Opcional: no menu `…` marque **Iniciar com o Windows**; clique com o botão direito no ícone da
   barra de tarefas → **Fixar na barra de tarefas**.

## Campo "quando?"

| Digite            | Significa                                     |
|-------------------|-----------------------------------------------|
| *(vazio)*         | sem horário (só uma anotação)                 |
| `14:30`, `9h`, `9h30` | hoje nesse horário (amanhã, se já passou) |
| `+15m`, `15 min`, `+2h`, `+1h30`, `em 2h`, `+1d` | daqui a X     |
| `amanhã`, `amanhã 14h`, `amanhã às 14:30` | amanhã (padrão 9h)    |
| `25/12`, `25/12 10:00`, `25/12/2027 10h` | data específica        |

## Lembretes recorrentes

Pelo botão ↻ ao lado do "quando?" (todo dia, dias úteis, toda semana, todo mês, a cada 15/30 min, 1/2 h)
ou digitando direto no campo:

| Digite                                  | Repete                          |
|-----------------------------------------|---------------------------------|
| `todo dia 9h`, `todos os dias às 18:30` | diariamente                     |
| `dias úteis 9h`, `seg a sex 8h`         | segunda a sexta                 |
| `toda segunda 10h`, `todo sábado 9h`    | semanalmente no dia indicado    |
| `todo dia 5 10h`, `todo mês dia 20`     | mensalmente (dia 31 → último dia nos meses curtos) |
| `a cada 2h`, `a cada 45m`, `a cada 1 dia` | em intervalos                 |

Sem horário, o padrão é 9h. Ao marcar como feito, clicar **Concluído** ou fechar o aviso, o lembrete
vai para a próxima ocorrência (não some da lista). Botão direito → **Pular esta vez** / **Parar de repetir**.

## Modo compacto perto da barra de tarefas

Se a janela estiver na metade de baixo da tela, ela expande **para cima** e recolhe para baixo,
mantendo a borda inferior no lugar.

## Atalhos

- **Enter** adiciona / salva · **Esc** limpa / cancela edição
- **Duplo clique** num lembrete edita · **botão direito** adia, remove horário ou exclui
- **×** esconde na bandeja (continua avisando) · **Sair** no menu `…` ou na bandeja fecha de vez

## Compilar

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\publish
```

O ícone é gerado por `tools\make-icon.ps1`.
