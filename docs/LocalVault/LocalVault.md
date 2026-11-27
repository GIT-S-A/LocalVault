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
are sealed with a passphrase. Shared entries are saved to a store, a KeePass database, defined on the PC or in the
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

### [Clear-VaultUserSecret](Clear-VaultUserSecret.md)

Removes the tracked values from the .NET user secrets.

### [Compare-VaultWithStore](Compare-VaultWithStore.md)

Compares the entries of a vault with its store.

### [Copy-VaultEntry](Copy-VaultEntry.md)

Copies a secret to the clipboard for a short time.

### [Copy-VaultToStore](Copy-VaultToStore.md)

Copies the shared entries of a vault once to another store.

### [Export-Vault](Export-Vault.md)

Exports every entry of a vault to a file sealed with a passphrase.

### [Export-VaultToStore](Export-VaultToStore.md)

Saves the shared entries of the repository's vaults to their stores.

### [Get-VaultEntry](Get-VaultEntry.md)

Gets the value of one vault entry.

### [Get-VaultEntryList](Get-VaultEntryList.md)

Gets the names, kinds and ages of the entries in a vault.

### [Get-VaultHelloStatus](Get-VaultHelloStatus.md)

Gets the state of the Windows Hello key on this PC and of the vault's Windows Hello entries.

### [Get-VaultSshKey](Get-VaultSshKey.md)

Gets the SSH keys of a vault, without their private keys.

### [Get-VaultSshKeyPath](Get-VaultSshKeyPath.md)

Gets the path of the key file of an SSH key entry.

### [Get-VaultStore](Get-VaultStore.md)

Gets the stores of the project and of this PC.

### [Get-VaultStoreStatus](Get-VaultStoreStatus.md)

Gets the store of each vault of the repository and what isn't saved yet.

### [Get-VaultTemplate](Get-VaultTemplate.md)

Gets the templated files of the repository and their state.

### [Get-VaultUserSecret](Get-VaultUserSecret.md)

Gets the vault value of a tracked .NET user secret.

### [Get-VaultUserSecretList](Get-VaultUserSecretList.md)

Gets the .NET user secrets of the repository and the state of each key.

### [Import-Vault](Import-Vault.md)

Merges entries from an export, a backup or a `secrets.local.clixml` file into a vault.

### [Import-VaultFromStore](Import-VaultFromStore.md)

Loads the shared entries of the repository's vaults from their stores.

### [Initialize-Vault](Initialize-Vault.md)

Adds the repository's vaults and the files written from templates to `.gitignore`.

### [Invoke-LocalVault](Invoke-LocalVault.md)

Runs a LocalVault command from a short command line.

### [Invoke-VaultScp](Invoke-VaultScp.md)

Runs scp with the key of an SSH key entry.

### [Invoke-VaultSsh](Invoke-VaultSsh.md)

Runs ssh with the key of an SSH key entry.

### [New-VaultSshKey](New-VaultSshKey.md)

Creates an SSH key pair for the hosts of a project and stores it in the vault.

### [Register-VaultStore](Register-VaultStore.md)

Defines a store on this PC or in the project.

### [Remove-VaultEntry](Remove-VaultEntry.md)

Deletes one entry from a vault.

### [Remove-VaultSshOrphan](Remove-VaultSshOrphan.md)

Deletes the SSH keys of clones that no longer exist on this PC.

### [Remove-VaultTemplate](Remove-VaultTemplate.md)

Stops managing a templated file.

### [Remove-VaultUserSecret](Remove-VaultUserSecret.md)

Stops tracking a .NET user secret.

### [Reset-VaultEntry](Reset-VaultEntry.md)

Removes the personal value of required entries so that a new one is asked.

### [Resolve-VaultEntry](Resolve-VaultEntry.md)

Gets a secret for a scope, or the shared secret when the scope has none.

### [Restore-VaultFile](Restore-VaultFile.md)

Writes file entries of a vault back to disk.

### [Set-VaultEntry](Set-VaultEntry.md)

Adds or replaces a secret or a file in a vault.

### [Set-VaultProjectStore](Set-VaultProjectStore.md)

Names the project's primary store.

### [Set-VaultSshInclude](Set-VaultSshInclude.md)

Adds or removes the line of `~/.ssh/config` that gives other OpenSSH tools the repository keys.

### [Set-VaultSshKeyHost](Set-VaultSshKeyHost.md)

Sets the hosts an SSH key of the vault is for.

### [Set-VaultSshTools](Set-VaultSshTools.md)

Sets or shows the OpenSSH folder that LocalVault uses on this PC.

### [Set-VaultStore](Set-VaultStore.md)

Changes one setting of a store without defining it again.

### [Set-VaultUserSecret](Set-VaultUserSecret.md)

Starts tracking a .NET user secret, or changes its value.

### [Unregister-VaultStore](Unregister-VaultStore.md)

Removes a store from this PC or from the project.

### [Update-VaultTemplate](Update-VaultTemplate.md)

Takes the edits of templated files back into their templates.

### [Use-VaultEnv](Use-VaultEnv.md)

Copies vault secrets into environment variables of the current process.

### [Write-VaultTemplate](Write-VaultTemplate.md)

Writes templated files from their templates and the vault.

### [Write-VaultUserSecret](Write-VaultUserSecret.md)

Writes the tracked values into the .NET user secrets.
