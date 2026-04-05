# AudioClient

Resoniteの音声専用ヘッドレスクライアントです。GUIなしでコンソールからResoniteセッションに参加し、音声処理を行うことができます。

## コマンド一覧

起動後、以下のコマンドをコンソールに入力して操作できます。

### `startWorldURL <recordURL>`

ワールドのレコードURLから新しいセッションを開始します。

```
startWorldURL resrec:///U-xxxxxxxx/R-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

- `resrec://` 形式のURLを指定してください
- セッションが開始されるとワールド名が表示されます

### `startWorldTemplate <templateName>`

組み込みテンプレートから新しいセッションを開始します。

```
startWorldTemplate Blank
startWorldTemplate Grid
startWorldTemplate Platform
```

引数なしで実行すると利用可能なテンプレート一覧を表示します。

| テンプレート名 | 説明 |
|---|---|
| `Blank` | 基本的な照明とスカイボックスのみのシンプルな空ワールド |
| `Grid` | グリッドテクスチャの床があるワールド |
| `Platform` | 円形の床プラットフォームがあるワールド |
| `DebugWorld` | デバッグ用ワールド |

### `name <name>`

現在フォーカスしているセッションの名前を変更します。

```
name My New World Name
```

- スペースを含む名前にも対応しています
- セッションにフォーカスしていない場合は実行できません

### `accessLevel <level>`

現在フォーカスしているセッションのアクセスレベルを設定します。

```
accessLevel Contacts
accessLevel Anyone
```

| レベル | 説明 |
|---|---|
| `Private` | ホストのみ |
| `LAN` | ローカルネットワーク内のみ |
| `Contacts` | 自分のコンタクトのみ |
| `ContactsPlus` | コンタクトとそのコンタクト |
| `RegisteredUsers` | 登録済みユーザー全員 |
| `Anyone` | 誰でも参加可能 |

- セッションにフォーカスしていない場合は実行できません

### `contactList`

現在オンラインの全コンタクトの名前・ステータス・参加中セッション名を一覧表示します。

```
contactList
```

出力例：
```
--- Online Contacts (3) ---
  SomeUser [Online] | Session: My Cool World
  AnotherUser [Away]
  FriendUser [Sociable] | Session: Chill Hangout
---
```

- `Offline` および `Invisible` のコンタクトは表示されません
- セッション名はアクセス可能な場合のみ表示されます（非公開セッションは表示なし）

### `contactInvite <username>`

コンタクトリストにいるユーザーを現在フォーカスしているセッションに招待します。

```
contactInvite SomeUser
```

- ログイン済みで、対象ユーザーがコンタクトとして承認済みである必要があります
- セッションにフォーカスしていない場合は実行できません
- 招待と同時に対象ユーザーをセッションの参加許可リストに追加します（アクセスレベルが制限されている場合でも参加可能になります）

### `contactInfo <username>`

コンタクトのオンライン状態と参加中のセッション情報を表示します。

```
contactInfo SomeUser
```

出力例：
```
--- Contact: SomeUser (ID: U-xxxxxxxx) ---
  Online Status: Online
  Sessions (1):
    - Access: Contacts [HOST]
  Current Session: My Cool World
    Host: SomeUser | Users: 3/16 | Access: Contacts
    Use 'contactJoin SomeUser' to join.
---
```

- セッション名はプライバシー設計によりセッション一覧には表示されません（アクセスレベルのみ）
- `CurrentSession` は自分がアクセス可能な場合のみ詳細が表示されます

### `contactJoin <username>`

コンタクトが現在いるセッションに参加します。

```
contactJoin SomeUser
```

- ログイン済みで、対象ユーザーがコンタクトとして承認済みである必要があります
- 相手のセッションが非公開またはアクセス不可の場合は参加できません
- `contactInfo` でセッション情報を確認してから実行することを推奨します

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

### `locomotion [name]`

現在のロコモーション（移動手段）を指定したものに切り替えます。名前を省略した場合は利用可能なロコモーション一覧を表示します。

```
locomotion
locomotion Noclip
locomotion fly
```

- 引数なしで実行すると、切り替え可能なロコモーションの一覧（現在 `(ACTIVE)` なものを含む）を表示します。
- `LocomotionName` またはクラス名（`NoclipLocomotion` など）の一部と一致する最初のモジュールに切り替えます（大文字・小文字を区別しません）。
- セッションにフォーカスしていない場合は実行できません。

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
