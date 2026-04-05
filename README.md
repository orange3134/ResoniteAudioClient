# AudioClient

Resoniteの音声専用ヘッドレスクライアントです。GUIなしでコンソールからResoniteセッションに参加し、音声処理を行うことができます。

## コマンド一覧

起動後、以下のコマンドをコンソールに入力して操作できます。

### `join <session_id/url>`

指定したセッションに参加します。

```
join S-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
join resrec:///S-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

- セッションIDのみを指定した場合、自動的に `resrec:///` プレフィックスが付与されます
- 完全なURLを直接指定することも可能です

### `activeSessions`

参加可能なセッションの一覧を表示します。

```
activeSessions
activeSessions --active
```

#### オプション

| オプション | 説明 |
|---|---|
| `--active` | 現在1人以上のユーザーがいるセッションのみ表示します |

表示される情報：
- セッション名
- ホストのユーザー名
- 参加人数 / 上限人数
- アクセスレベル
- セッションURL（`join`コマンドにコピペして使用可能）

出力例：
```
--- Active Sessions (2) ---
  1. My Cool World
     Host: HostUser | Users: 3/16 | Access: Anyone
     URL: lnl-nat://xxxx-xxxx/S-xxxx...
  2. Another Session
     Host: AnotherHost | Users: 1/8 | Access: Friends
     URL: lnl-nat://yyyy-yyyy/S-yyyy...
--- End of sessions ---
```

### `currentSessions`

現在自分が接続しているセッションの一覧とインデックス番号を表示します。

```
currentSessions
```

表示される情報：
- インデックス番号（`focus`コマンドで使用）
- セッション名
- フォーカス状態 (`[FOCUSED]`)
- ワールドの状態
- ユーザー数
- セッションID

出力例：
```
--- Connected Sessions (2) ---
  1. My World [FOCUSED] | State: Running | Users: 3 | Session: S-xxxx...
  2. Another World | State: Running | Users: 2 | Session: S-yyyy...
--- End of sessions ---
```

### `focus <index>`

`currentSessions`で表示されたインデックス番号を指定して、そのセッションにフォーカスを切り替えます。

```
focus 1
focus 2
```

- `currentSessions`で表示されるインデックス番号を指定してください
- フォーカスすると、そのセッションが`users`や`moveToUser`などの対象になります
- Running状態のセッションのみフォーカス可能です

### `users`

現在フォーカスしているセッション内の全ユーザー情報を一覧表示します。

```
users
```

表示される情報：
- ユーザー名
- ユーザーID
- ホスト状態 (`[HOST]`)
- プレゼンス状態 (`Present` / `Away`)
- Ping値

出力例：
```
--- Users in 'My World' (Session: S-xxxx...) ---
  1. HostUser [HOST] | ID: U-hostuser | Present | Ping: 12ms
  2. AudioClient (You) | ID: U-audioclient | Present | Ping: 0ms
  3. AnotherUser | ID: U-anotheruser | Present | Ping: 45ms
--- Total: 3 user(s) ---
```

### `moveToUser <userName>`

指定したユーザーの目の前1mの位置に移動し、そのユーザーの方を向きます。

```
moveToUser HostUser
moveToUser Some User Name
```

- ユーザー名の大文字・小文字は区別しません
- スペースを含むユーザー名にも対応しています
- 指定したユーザーが見つからない場合、セッション内のユーザー一覧が表示されます
- 自分自身への移動はできません

### `leave`

現在フォーカスしているセッションから切断します。

```
leave
```

- クライアントとして参加している場合はセッションから離脱します
- ホストとして参加している場合はセッションを終了します

### `login <username> <password>`

Resoniteにログインします。

```
login myUsername myPassword
```

- ユーザー名またはメールアドレスで認証できます
- すでにログイン済みの場合は先に `logout` が必要です
- ログイン処理は非同期で実行され、完了時に結果が表示されます

### `logout`

現在のResoniteアカウントからログアウトします。

```
logout
```

- ログインしていない場合はその旨が表示されます

### `mute`

マイクのミュート状態をトグル（切り替え）します。

```
mute
```

- ミュート中に実行するとミュート解除
- ミュート解除中に実行するとミュート

### `exit` / `quit`

AudioClientを終了します。

```
exit
quit
```
