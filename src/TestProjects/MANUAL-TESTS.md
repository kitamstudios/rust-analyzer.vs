# Build & Debug UI

- Solution explorer
  - main.rs file context menu => shows 'Debug' & 'Set as startup item'
  - Cargo.toml file context menu => shows 'Build', 'Clean', 'Debug' & 'Set as startup item'

- Toolbar > Select startup item drop down => shows 'Current document' | 'hello_world.exe'

- Toolbar > Select startup item drop down > 'Current document'
  - open Cargo.toml file
    => 'Current Document (main.rs)'
    => dev | release
    => F5 works
    => Ctrl+B works
    => Alt+B > Clean works
  - same for Cargo.toml

- Toolbar > Select startup item drop down > 'hello_world.exe'
  => release | debug
  => F5 works
  => Ctrl+B works
  => Alt+B > Clean works

