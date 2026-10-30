---
document type: module
Help Version: 1.0.0
HelpInfoUri: ''
Locale: en-US
Module Guid: 2a3606de-c8ef-4b0f-aac5-ce7b3ee85c88
Module Name: LocalVault
ms.date: 10/13/2026
PlatyPS schema version: 2024-05-01
title: LocalVault Module
---

# LocalVault Module

## Description

The LocalVault module stores the secrets and files of a project in one vault file in the project
folder. The file is sealed with Windows DPAPI for the current user on the current machine. Exports
are sealed with a passphrase. Automatic backups are sealed with a passphrase and a keyfile kept in
the vault. Shared entries are saved to a store, a KeePass database, defined on the PC or in the
project, where the team reads them and from which a new machine loads them.

The module requires Windows and PowerShell 7.6 or later. For concepts, see
[about_LocalVault](about_LocalVault.md).

## LocalVault

### [Add-VaultTemplate](Add-VaultTemplate.md)

Turns a file that holds secrets into a committed template.

### [Clear-Vault](Clear-Vault.md)

Deletes a vault file and its previous version.

### [Clear-VaultTemplate](Clear-VaultTemplate.md)

Deletes the files written from templates.

### [Compare-VaultWithStore](Compare-VaultWithStore.md)

Compares the entries of a vault with its store.

### [Copy-VaultEntry](Copy-VaultEntry.md)

Copies a secret to the clipboard for a short time.

### [Copy-VaultToStore](Copy-VaultToStore.md)

Copies the shared entries of a vault once to another store.

### [Disable-VaultBackup](Disable-VaultBackup.md)

Turns off automatic backups of a vault.

### [Enable-VaultBackup](Enable-VaultBackup.md)

Turns on automatic backups of a vault.

### [Export-Vault](Export-Vault.md)

Exports every entry of a vault to a file sealed with a passphrase.

### [Export-VaultToStore](Export-VaultToStore.md)

Saves the shared entries of the repository's vaults to their stores.

### [Get-VaultBackup](Get-VaultBackup.md)

Gets the state of a vault's automatic backups.

### [Get-VaultEntry](Get-VaultEntry.md)

Gets the value of one vault entry.

### [Get-VaultEntryList](Get-VaultEntryList.md)

Gets the names, kinds and ages of the entries in a vault.

### [Get-VaultStore](Get-VaultStore.md)

Gets the stores of the project and of this PC.

### [Get-VaultStoreStatus](Get-VaultStoreStatus.md)

Gets the store of each vault of the repository and what isn't saved yet.

### [Get-VaultTemplate](Get-VaultTemplate.md)

Gets the templated files of the repository and their state.

### [Import-Vault](Import-Vault.md)

Merges entries from an export, a backup or a `secrets.local.clixml` file into a vault.

### [Import-VaultFromStore](Import-VaultFromStore.md)

Loads the shared entries of the repository's vaults from their stores.

### [Invoke-LocalVault](Invoke-LocalVault.md)

Runs a LocalVault command from a short command line.

### [Invoke-VaultBackup](Invoke-VaultBackup.md)

Writes a backup of a vault now.

### [Register-VaultStore](Register-VaultStore.md)

Defines a store on this PC or in the project.

### [Remove-VaultEntry](Remove-VaultEntry.md)

Deletes one entry from a vault.

### [Remove-VaultTemplate](Remove-VaultTemplate.md)

Stops managing a templated file.

### [Resolve-VaultEntry](Resolve-VaultEntry.md)

Gets a secret for a scope, or the shared secret when the scope has none.

### [Restore-VaultFile](Restore-VaultFile.md)

Writes file entries of a vault back to disk.

### [Set-VaultEntry](Set-VaultEntry.md)

Adds or replaces a secret or a file in a vault.

### [Set-VaultProjectStore](Set-VaultProjectStore.md)

Names the project's primary store.

### [Set-VaultStore](Set-VaultStore.md)

Changes one setting of a store without defining it again.

### [Test-VaultBackup](Test-VaultBackup.md)

Verifies that the newest backup of a vault opens.

### [Unregister-VaultStore](Unregister-VaultStore.md)

Removes a store from this PC or from the project.

### [Update-VaultTemplate](Update-VaultTemplate.md)

Takes the edits of templated files back into their templates.

### [Use-VaultEnv](Use-VaultEnv.md)

Copies vault secrets into environment variables of the current process.

### [Write-VaultTemplate](Write-VaultTemplate.md)

Writes templated files from their templates and the vault.
