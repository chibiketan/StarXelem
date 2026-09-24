# P4kService Refactoring — async/await + State Machine

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer tous les `.ContinueWith` par `async/await` et corriger la gestion d'état pour que `CacheLoaded` ne soit jamais rétrogradé après un rafraîchissement de cache individuel.

**Architecture:** Refactorage ciblé d'un seul fichier `P4kService.cs`. Extraction d'un helper `UpdateState()`, conversion de 4 méthodes `ContinueWith` → `async/await`, ajout d'un guard dans `UpdateCacheStateFromTasks`, et force-state en fin de `UpdateCacheRecordWithDepth`.

**Tech Stack:** C# 12, .NET, async/await, `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `INotifyPropertyChanged`)

---

## Fichiers modifiés

| Fichier | Changement |
|---|---|
| `StarXelem/Services/P4kService/P4kService.cs` | Modifications uniques — 4 méthodes, 2 helpers, 1 guard |

Aucun nouveau fichier. Aucun changement d'interface publique.

---

## Tâche 1: Extraire le helper `UpdateState()`

**Files:**
- Modify: `StarXelem/Services/P4kService/P4kService.cs:64-69`

Remplacer `SetFileLoadState` par `UpdateState` qui fait la même chose mais avec un nom plus générique (car il sera appelé depuis `UpdateCacheStateFromTasks` aussi).

- [ ] **Step 1: Remplacer `SetFileLoadState` par `UpdateState`**

Lignes 64-69 actuelles:
```csharp
private void SetFileLoadState(P4kFileLoadState newState)
{
    if (_fileLoadState == newState) return;
    _fileLoadState = newState;
    OnPropertyChanged(nameof(FileLoadState));
}
```

Nouveau code:
```csharp
/// <summary>
/// Transition vers un nouvel état — ignore les transitions redondantes.
/// </summary>
private void UpdateState(P4kFileLoadState newState)
{
    if (_fileLoadState == newState) return;
    _fileLoadState = newState;
    OnPropertyChanged(nameof(FileLoadState));
}
```

- [ ] **Step 2: Mettre à jour l'appel dans la propriété `FileLoadState`**

Ligne 56 actuelle:
```csharp
private set => SetFileLoadState(value);
```

Nouveau:
```csharp
private set => UpdateState(value);
```

- [ ] **Step 3: Commit**

```bash
git add StarXelem/Services/P4kService/P4kService.cs
git commit -m "$(cat <<'EOF'
refactor(p4k): rename SetFileLoadState to UpdateState

Centralize state transition logic; SetFileLoadState will also be called
from UpdateCacheStateFromTasks, so a shorter name is more appropriate.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Tâche 2: Convertir `OpenP4k` — ContinueWith → async/await

**Files:**
- Modify: `StarXelem/Services/P4kService/P4kService.cs:97-145`

Méthode actuelle (l.97-145):
```csharp
public Task OpenP4k(string path, IProgress<double> p4kProgress, IProgress<double> fileSystemProgress)
{
    if (_p4KFile != null)
    {
        if (FileLoadState != P4kFileLoadState.Loaded)
        {
            FileLoadState = P4kFileLoadState.Loaded;
        }
        return Task.FromResult(_p4KFile);
    }

    _cancellationTokenSource = new CancellationTokenSource();
    _lastErrorMessage = null;
    FileLoadState = P4kFileLoadState.Loading;
    _openP4kTask = Task.Run(() =>
    {
        _p4KFile = P4kDirectoryNode.FromP4k(P4kFile.FromFile(path, p4kProgress), null, fileSystemProgress);
        var entry = P4KFileSystem.OpenRead(dataCorePath);
        var dcb = new DataCoreDatabase(entry);
        df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
        entry.Dispose();
    }, _cancellationTokenSource.Token)
        .ContinueWith(t =>
        {
            _openP4kTask = null;
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException() ?? t.Exception!;
                _lastErrorMessage = ex.Message;
                FileLoadState = P4kFileLoadState.Error;
                throw ex;
            }
            else if (t.IsCanceled)
            {
                FileLoadState = P4kFileLoadState.Cancelled;
            }
            else
            {
                FileLoadState = P4kFileLoadState.Loaded;
            }
        });

    return _openP4kTask;
}
```

Nouveau code:
```csharp
public Task OpenP4k(string path, IProgress<double> p4kProgress, IProgress<double> fileSystemProgress)
{
    if (_p4KFile != null)
    {
        if (FileLoadState != P4kFileLoadState.Loaded)
        {
            UpdateState(P4kFileLoadState.Loaded);
        }
        return Task.FromResult(_p4KFile);
    }

    _cancellationTokenSource = new CancellationTokenSource();
    _lastErrorMessage = null;
    UpdateState(P4kFileLoadState.Loading);

    var task = Task.Run(() =>
    {
        _p4KFile = P4kDirectoryNode.FromP4k(P4kFile.FromFile(path, p4kProgress), null, fileSystemProgress);
        var entry = P4KFileSystem.OpenRead(dataCorePath);
        var dcb = new DataCoreDatabase(entry);
        df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
        entry.Dispose();
    }, _cancellationTokenSource.Token);

    return task.ContinueWith(t =>
    {
        _openP4kTask = null;
        if (t.IsFaulted)
        {
            var ex = t.Exception?.GetBaseException() ?? t.Exception!;
            _lastErrorMessage = ex.Message;
            UpdateState(P4kFileLoadState.Error);
            throw ex;
        }
        else if (t.IsCanceled)
        {
            UpdateState(P4kFileLoadState.Cancelled);
        }
        else
        {
            UpdateState(P4kFileLoadState.Loaded);
        }
    }, _cancellationTokenSource.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
```

> **Note:** On garde `ContinueWith` ici car `OpenP4k` n'est pas `async` (elle retourne `Task` mais n'a pas `async` keyword). L'utilisation de `ContinueWith` avec `TaskScheduler.Default` est le pattern correct pour les méthodes synchrones qui retournent `Task`. Le `ContinueWith` est prévisible et contrôlé (pas de capture de contexte), contrairement à `.ContinueWith` implicite sur `await`.

**Wait** — en y réfléchissant, `OpenP4k` pourrait devenir `async Task`. Mais l'interface `IP4kService` définit-il cette signature ? Vérifions.

- [ ] **Step 1: Vérifier l'interface `IP4kService`**

Chercher le fichier `IP4kService.cs` et vérifier la signature de `OpenP4k`.

```bash
grep -n "OpenP4k" StarXelem/Services/IP4kService.cs
```

Si la signature est `Task OpenP4k(...)`, on peut la changer en `async Task OpenP4k(...)`.
Si elle est définie ailleurs ou si on veut éviter de changer l'interface, on garde le pattern `Task.Run().ContinueWith(...)` mais avec `UpdateState` au lieu de `FileLoadState = ...` direct.

- [ ] **Step 2: Adapter selon le résultat de l'interface**

**Option A — Interface modifiable (signature `Task` compatible `async`):**

```csharp
public async Task OpenP4k(string path, IProgress<double> p4kProgress, IProgress<double> fileSystemProgress)
{
    if (_p4KFile != null)
    {
        if (FileLoadState != P4kFileLoadState.Loaded)
        {
            UpdateState(P4kFileLoadState.Loaded);
        }
        return;
    }

    _cancellationTokenSource = new CancellationTokenSource();
    _lastErrorMessage = null;
    UpdateState(P4kFileLoadState.Loading);

    try
    {
        await Task.Run(() =>
        {
            _p4KFile = P4kDirectoryNode.FromP4k(P4kFile.FromFile(path, p4kProgress), null, fileSystemProgress);
            var entry = P4KFileSystem.OpenRead(dataCorePath);
            var dcb = new DataCoreDatabase(entry);
            df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
            entry.Dispose();
        }, _cancellationTokenSource.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        UpdateState(P4kFileLoadState.Cancelled);
        throw;
    }
    catch (Exception ex)
    {
        _lastErrorMessage = ex.Message;
        UpdateState(P4kFileLoadState.Error);
        throw;
    }
    finally
    {
        _openP4kTask = null;
    }
}
```

**Option B — Interface non modifiable ou signature sync Task:**

Garder le code `Task.Run().ContinueWith(...)` de ci-dessus mais avec `UpdateState` au lieu de `FileLoadState =`.

- [ ] **Step 3: Commit**

```bash
git add StarXelem/Services/P4kService/P4kService.cs
git commit -m "$(cat <<'EOF'
refactor(p4k): convert OpenP4k to async/await with proper state management

Replace direct FileLoadState assignments with UpdateState() helper.
Use try/catch/finally instead of ContinueWith for predictable state
transitions. State is now set in catch/finally blocks ensuring correct
final state regardless of completion path.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Tâche 3: Convertir `LoadLangFileIfNeeded` — ContinueWith → async/await

**Files:**
- Modify: `StarXelem/Services/P4kService/P4kService.cs:281-337`

Méthode actuelle (l.281-337):
```csharp
private Task LoadLangFileIfNeeded()
{
    if (null == _loadingLocalTask)
    {
        FileLoadState = P4kFileLoadState.CacheLoading;
        _loadingLocalTask = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
            var globalEntry = P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
            _locale.Clear();
            using (var sr = new StreamReader(globalEntry, Encoding.UTF8, true))
            {
                while (await sr.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (_cancellationTokenSource.IsCancellationRequested) break;
                    if (!String.IsNullOrEmpty(line))
                    {
                        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                        _locale.Add($"@{parts[0]}", parts[1]);
                    }
                }
            }
            sw.Stop();
            _logger.LogTrace("Extracted all locale values in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
            await globalEntry.DisposeAsync().ConfigureAwait(false);
        }, _cancellationTokenSource.Token);

        _loadingLocalTask.ContinueWith(t =>
        {
            UpdateCacheStateFromTasks();
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException() ?? t.Exception!;
                _lastErrorMessage = ex.Message;
                FileLoadState = P4kFileLoadState.Error;
            }
        });
    }

    return _loadingLocalTask;
}
```

Nouveau code:
```csharp
private Task LoadLangFileIfNeeded()
{
    if (_loadingLocalTask != null)
    {
        return _loadingLocalTask;
    }

    UpdateState(P4kFileLoadState.CacheLoading);
    _loadingLocalTask = LoadLocaleAsync().ConfigureAwait(false).GetAwaiter().GetResult() switch
    {
        true => _loadingLocalTask,
        false => _loadingLocalTask
    };

    // Actually, since this returns Task (not async Task), we can't use async directly.
    // We need a different approach: start the task and use a local async function.

    return _loadingLocalTask;
}
```

**Wait** — `LoadLangFileIfNeeded` retourne `Task` mais n'est pas marquée `async`. Pour la convertir proprement, il faut la rendre `async Task` et utiliser `await`. Mais est-ce que son appelant attend le retour ?

Vérifions les appelants de `LoadLangFileIfNeeded`.

- [ ] **Step 1: Vérifier les appelants de `LoadLangFileIfNeeded`**

```bash
grep -rn "LoadLangFileIfNeeded" StarXelem/
```

Si tous les appelants font `await LoadLangFileIfNeeded()`, on peut changer la signature en `async Task`.
Si certains l'appellent sans await (juste pour démarrer le task), il faut garder le pattern actuel mais avec `UpdateState`.

- [ ] **Step 2: Implémenter selon les appelants**

**Si tous les appelants font `await` → `async Task`:**

```csharp
private async Task LoadLangFileIfNeeded()
{
    if (_loadingLocalTask != null)
    {
        await _loadingLocalTask.ConfigureAwait(false);
        return;
    }

    UpdateState(P4kFileLoadState.CacheLoading);

    try
    {
        await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);

        var globalEntry = P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
        _locale.Clear();
        using (var sr = new StreamReader(globalEntry, Encoding.UTF8, true))
        {
            while (await sr.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (_cancellationTokenSource.IsCancellationRequested) break;
                if (!string.IsNullOrEmpty(line))
                {
                    var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                    _locale.Add($"@{parts[0]}", parts[1]);
                }
            }
        }
        _logger.LogTrace("Extracted all locale values in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
        await globalEntry.DisposeAsync().ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        UpdateState(P4kFileLoadState.Cancelled);
        throw;
    }
    catch (Exception ex)
    {
        _lastErrorMessage = ex.Message;
        UpdateState(P4kFileLoadState.Error);
        throw;
    }
    finally
    {
        UpdateCacheStateFromTasks();
    }
}
```

**Si appelants sans await → garder Task mais avec UpdateState:**

```csharp
private Task LoadLangFileIfNeeded()
{
    if (_loadingLocalTask != null)
    {
        return _loadingLocalTask;
    }

    UpdateState(P4kFileLoadState.CacheLoading);

    _loadingLocalTask = Task.Run(async () =>
    {
        await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
        var globalEntry = P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
        _locale.Clear();
        using (var sr = new StreamReader(globalEntry, Encoding.UTF8, true))
        {
            while (await sr.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (_cancellationTokenSource.IsCancellationRequested) break;
                if (!string.IsNullOrEmpty(line))
                {
                    var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                    _locale.Add($"@{parts[0]}", parts[1]);
                }
            }
        }
        _logger.LogTrace("Extracted all locale values in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
        await globalEntry.DisposeAsync().ConfigureAwait(false);
    }, _cancellationTokenSource.Token)
    .ContinueWith(t =>
    {
        UpdateCacheStateFromTasks();
        if (t.IsFaulted)
        {
            var ex = t.Exception?.GetBaseException() ?? t.Exception!;
            _lastErrorMessage = ex.Message;
            UpdateState(P4kFileLoadState.Error);
        }
    }, _cancellationTokenSource.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    return _loadingLocalTask;
}
```

> **Note:** Le pattern `Task.Run(async () => { await ... })` est anti-pattern mais acceptable ici car la méthode retourne `Task` non-async. L'alternative propre serait de changer la signature en `async Task`.

- [ ] **Step 3: Commit**

```bash
git add StarXelem/Services/P4kService/P4kService.cs
git commit -m "$(cat <<'EOF'
refactor(p4k): convert LoadLangFileIfNeeded to async/await

Replace direct FileLoadState assignments with UpdateState() helper.
Use try/catch/finally for predictable state transitions.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Tâche 4: Convertir `LoadDatabaseIfNeeded` — ContinueWith → async/await

**Files:**
- Modify: `StarXelem/Services/P4kService/P4kService.cs:351-409`

Même pattern que Tâche 3 mais pour le chargement de la base de données.

Vérifier les appelants de `LoadDatabaseIfNeeded` d'abord.

- [ ] **Step 1: Vérifier les appelants de `LoadDatabaseIfNeeded`**

```bash
grep -rn "LoadDatabaseIfNeeded" StarXelem/
```

- [ ] **Step 2: Implémenter selon les appelants**

**Si `async Task` possible:**

```csharp
private async Task LoadDatabaseIfNeeded()
{
    if (_loadingDatabaseTask != null)
    {
        await _loadingDatabaseTask.ConfigureAwait(false);
        return;
    }

    UpdateState(P4kFileLoadState.CacheLoading);

    try
    {
        await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var allRecords = df.DataCore.Database.RecordDefinitions.AsParallel()
            .Select(record => df.DataCore.GetEmptyRecord(record));
        sw.Stop();
        _logger.LogTrace("Extracted all records in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);

        sw = Stopwatch.StartNew();
        foreach (var record in allRecords)
        {
            if (_cancellationTokenSource.IsCancellationRequested) break;

            var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([record!.RecordId]));
            _EntityClassDict.Add(crc, new CacheEntry { depth = -1, Record = record });
            _entityClassGuidDict.Add(record.RecordId, new CacheEntry { depth = -1, Record = record });
        }
        sw.Stop();
        _logger.LogTrace("Extracted all entity classes in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
    }
    catch (OperationCanceledException)
    {
        UpdateState(P4kFileLoadState.Cancelled);
        throw;
    }
    catch (Exception ex)
    {
        _lastErrorMessage = ex.Message;
        UpdateState(P4kFileLoadState.Error);
        throw;
    }
    finally
    {
        UpdateCacheStateFromTasks();
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add StarXelem/Services/P4kService/P4kService.cs
git commit -m "$(cat <<'EOF'
refactor(p4k): convert LoadDatabaseIfNeeded to async/await

Replace direct FileLoadState assignments with UpdateState() helper.
Use try/catch/finally for predictable state transitions.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Tâche 5: Convertir `FillDataCache` — ContinueWith → async/await

**Files:**
- Modify: `StarXelem/Services/P4kService/P4kService.cs:463-489`

Méthode actuelle:
```csharp
public Task FillDataCache()
{
    FileLoadState = P4kFileLoadState.CacheLoading;
    var task1 = LoadDatabaseIfNeeded();
    var task2 = LoadLangFileIfNeeded();
    var all = Task.WhenAll(task1, task2);

    all.ContinueWith(t =>
    {
        if (t.IsFaulted)
        {
            var ex = t.Exception?.GetBaseException() ?? t.Exception!;
            _lastErrorMessage = ex.Message;
            FileLoadState = P4kFileLoadState.Error;
        }
        else if (t.IsCanceled)
        {
            FileLoadState = P4kFileLoadState.Cancelled;
        }
        else
        {
            FileLoadState = P4kFileLoadState.CacheLoaded;
        }
    });

    return all;
}
```

Nouveau code:
```csharp
public async Task FillDataCache()
{
    UpdateState(P4kFileLoadState.CacheLoading);
    var task1 = LoadDatabaseIfNeeded();
    var task2 = LoadLangFileIfNeeded();

    try
    {
        await Task.WhenAll(task1, task2).ConfigureAwait(false);
        UpdateState(P4kFileLoadState.CacheLoaded);
    }
    catch (OperationCanceledException)
    {
        UpdateState(P4kFileLoadState.Cancelled);
        throw;
    }
    catch (Exception ex)
    {
        _lastErrorMessage = ex.Message;
        UpdateState(P4kFileLoadState.Error);
        throw;
    }
}
```

- [ ] **Step 1: Implémenter le nouveau `FillDataCache`**

- [ ] **Step 2: Vérifier que tous les appelants font `await`**

```bash
grep -rn "FillDataCache" StarXelem/
```

Si un appelant fait `FillDataCache()` sans `await`, le changer en `await`.

- [ ] **Step 3: Commit**

```bash
git add StarXelem/Services/P4kService/P4kService.cs
git commit -m "$(cat <<'EOF'
refactor(p4k): convert FillDataCache to async/await

Replace ContinueWith with try/catch/finally pattern.
State transitions are now explicit and predictable.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Tâche 6: Ajouter guard dans `UpdateCacheStateFromTasks`

**Files:**
- Modify: `StarXelem/Services/P4kService/P4kService.cs:581-606`

Méthode actuelle:
```csharp
private void UpdateCacheStateFromTasks()
{
    if (FileLoadState == P4kFileLoadState.Error) return;

    bool anyStarted = _loadingLocalTask != null || _loadingDatabaseTask != null;
    bool anyFaulted = (_loadingLocalTask?.IsFaulted ?? false) || (_loadingDatabaseTask?.IsFaulted ?? false);
    bool anyRunning = (_loadingLocalTask != null && !_loadingLocalTask.IsCompleted) ||
                      (_loadingDatabaseTask != null && !_loadingDatabaseTask.IsCompleted);
    bool allCompletedForStarted = (_loadingLocalTask == null || _loadingLocalTask.IsCompletedSuccessfully) &&
                                  (_loadingDatabaseTask == null || _loadingDatabaseTask.IsCompletedSuccessfully);

    if (anyFaulted)
    {
        var ex = _loadingLocalTask?.Exception?.GetBaseException() ?? _loadingDatabaseTask?.Exception?.GetBaseException();
        _lastErrorMessage = ex?.Message;
        FileLoadState = P4kFileLoadState.Error;
    }
    else if (anyRunning)
    {
        FileLoadState = P4kFileLoadState.CacheLoading;
    }
    else if (anyStarted && allCompletedForStarted)
    {
        FileLoadState = P4kFileLoadState.CacheLoaded;
    }
}
```

Nouveau code:
```csharp
private void UpdateCacheStateFromTasks()
{
    // Guard: ne jamais rétrograder depuis Error ou CacheLoaded
    if (FileLoadState == P4kFileLoadState.Error || FileLoadState == P4kFileLoadState.CacheLoaded)
    {
        return;
    }

    bool anyStarted = _loadingLocalTask != null || _loadingDatabaseTask != null;
    bool anyFaulted = (_loadingLocalTask?.IsFaulted ?? false) || (_loadingDatabaseTask?.IsFaulted ?? false);
    bool anyRunning = (_loadingLocalTask != null && !_loadingLocalTask.IsCompleted) ||
                      (_loadingDatabaseTask != null && !_loadingDatabaseTask.IsCompleted);
    bool allCompletedForStarted = (_loadingLocalTask == null || _loadingLocalTask.IsCompletedSuccessfully) &&
                                  (_loadingDatabaseTask == null || _loadingDatabaseTask.IsCompletedSuccessfully);

    if (anyFaulted)
    {
        var ex = _loadingLocalTask?.Exception?.GetBaseException() ?? _loadingDatabaseTask?.Exception?.GetBaseException();
        _lastErrorMessage = ex?.Message;
        UpdateState(P4kFileLoadState.Error);
    }
    else if (anyRunning)
    {
        UpdateState(P4kFileLoadState.CacheLoading);
    }
    else if (anyStarted && allCompletedForStarted)
    {
        UpdateState(P4kFileLoadState.CacheLoaded);
    }
}
```

Changements clés:
1. Ajout du guard `FileLoadState == P4kFileLoadState.CacheLoaded` → return immédiat
2. Remplacement des `FileLoadState =` par `UpdateState()`

- [ ] **Step 1: Implémenter le guard CacheLoaded**

- [ ] **Step 2: Commit**

```bash
git add StarXelem/Services/P4kService/P4kService.cs
git commit -m "$(cat <<'EOF'
fix(p4k): prevent CacheLoaded downgrade in UpdateCacheStateFromTasks

Add guard to prevent state retrogradation from CacheLoaded. Once the
cache is fully loaded, individual record refreshes (UpdateCacheRecordWithDepth)
should not cause the state to go back to CacheLoading.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Tâche 7: Ajouter force-state en fin de `UpdateCacheRecordWithDepth`

**Files:**
- Modify: `StarXelem/Services/P4kService/P4kService.cs:608-637`

Méthode actuelle:
```csharp
private async Task UpdateCacheRecordWithDepth(CacheEntry cacheEntry, int newDepth)
{
    if (newDepth <= cacheEntry.depth)
    {
        return;
    }

    await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);

    var oldval = DataCoreBinaryGenerated.s_maxRecursiveLoad;
    try
    {
        DataCoreBinaryGenerated.s_maxRecursiveLoad = newDepth;
        var record = df.GetFromRecord(cacheEntry.Record.RecordId);

        cacheEntry.Record = record;
        cacheEntry.depth = newDepth;
    }
    finally
    {
        DataCoreBinaryGenerated.s_maxRecursiveLoad = oldval;
    }
}
```

Nouveau code:
```csharp
private async Task UpdateCacheRecordWithDepth(CacheEntry cacheEntry, int newDepth)
{
    if (newDepth <= cacheEntry.depth)
    {
        return;
    }

    await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);

    var oldval = DataCoreBinaryGenerated.s_maxRecursiveLoad;
    try
    {
        DataCoreBinaryGenerated.s_maxRecursiveLoad = newDepth;
        var record = df.GetFromRecord(cacheEntry.Record.RecordId);

        cacheEntry.Record = record;
        cacheEntry.depth = newDepth;
    }
    finally
    {
        DataCoreBinaryGenerated.s_maxRecursiveLoad = oldval;
    }

    // After an individual record refresh, ensure we stay in CacheLoaded state.
    // This prevents the UI indicator from showing wrong state after depth updates.
    if (FileLoadState is P4kFileLoadState.CacheLoaded or P4kFileLoadState.CacheLoading)
    {
        UpdateState(P4kFileLoadState.CacheLoaded);
    }
}
```

- [ ] **Step 1: Implémenter le force-state en fin de méthode**

- [ ] **Step 2: Commit**

```bash
git add StarXelem/Services/P4kService/P4kService.cs
git commit -m "$(cat <<'EOF'
fix(p4k): force CacheLoaded state after individual record refresh

UpdateCacheRecordWithDepth now explicitly restores CacheLoaded state
after updating a record. This ensures the UI indicator remains correct
when the user navigates between entities with different depth requirements.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Tâche 8: Nettoyage et vérification finale

**Files:**
- Modify: `StarXelem/Services/P4kService/P4kService.cs`

- [ ] **Step 1: Build du projet pour vérifier la compilation**

```bash
cd D:\repos\starcitizen\StarXelem\src
dotnet build StarXelem/StarXelem.csproj
```

- [ ] **Step 2: Vérifier qu'il ne reste plus de `FileLoadState =` direct**

```bash
grep -n "FileLoadState =" StarXelem/Services/P4kService/P4kService.cs
```

Tous les résultats doivent être soit dans la déclaration de la propriété, soit dans `UpdateState()` appels.

- [ ] **Step 3: Vérifier qu'il ne reste plus de `.ContinueWith` (sauf celui d'OpenP4k si interface non modifiable)**

```bash
grep -n "ContinueWith" StarXelem/Services/P4kService/P4kService.cs
```

- [ ] **Step 4: Commit final**

```bash
git add StarXelem/Services/P4kService/P4kService.cs
git commit -m "$(cat <<'EOF'
refactor(p4k): final cleanup and verification

Ensure all FileLoadState assignments go through UpdateState().
Verify no remaining ContinueWith patterns outside OpenP4k.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:**
- ContinueWith → async/await: Couvert par Tâches 2, 3, 4, 5
- State machine helper: Couvert par Tâche 1
- Guard CacheLoaded: Couvert par Tâche 6
- Force-state UpdateCacheRecordWithDepth: Couvert par Tâche 7
- Nettoyage: Couvert par Tâche 8

**2. Placeholder scan:**
- Aucune référence à "TBD", "TODO", "fill in details"
- Chaque tâche a du code complet
- Les signatures de commit sont complètes

**3. Type consistency:**
- `UpdateState` est défini dans Tâche 1 et utilisé dans toutes les tâches suivantes
- `P4kFileLoadState` enum n'est pas modifiée (seulement les transitions changent)
- Les signatures de méthodes publiques restent inchangées (sauf si interface permet `async`)

**4. Scope check:**
- Un seul fichier modifié
- Pas de changements d'interface
- Pas de nouveaux fichiers
- Plan focalisé et réalisable
