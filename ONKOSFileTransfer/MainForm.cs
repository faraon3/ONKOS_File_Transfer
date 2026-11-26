foreach (PeerInfo target in activeTargets)
                {
                    string batchId = Guid.NewGuid().ToString("N");
                    string senderDisplayName = _localProfile != null && !string.IsNullOrWhiteSpace(_localProfile.FullName)
                        ? _localProfile.FullName
                        : Environment.MachineName;

                    foreach (var entry in preparedPayloads)
                    {
                        entry.SourceItem.State = QueueItemDeliveryState.Sending;
                        RefreshFilesListView();

                        SetStatus($"Enviando '{entry.DisplayName}' a {target.FullName}...");
                        long baseBytesForItem = sentBytesSoFar;

                        try
                        {
                            await FileSender.SendFileAsync(
                                target.IPv4,
                                TransferPort,
                                entry.PayloadPath,
                                (sent, total) =>
                                {
                                    long currentTotalSent = baseBytesForItem + sent;
                                    int pct = (int)Math.Round((double)currentTotalSent / grandTotalBytes * 100.0);

                                    string etaStr = string.Empty;
                                    double elapsedSec = sw.Elapsed.TotalSeconds;
                                    if (currentTotalSent > 0 && elapsedSec > 0.35)
                                    {
                                        double bytesPerSec = currentTotalSent / elapsedSec;
                                        long remainingBytes = Math.Max(0, grandTotalBytes - currentTotalSent);
                                        etaStr = FormatEta(remainingBytes / bytesPerSec);
                                    }

                                    SetProgress(pct, etaStr);
                                },
                                batchId,
                                senderDisplayName);

                            target.LastSeenUtc = DateTime.UtcNow;
                            successCount++;

                            TransferHistoryStore.AppendEntry(
                                "ENVIADO",
                                entry.DisplayName,
                                entry.Size,
                                target.DisplayText,
                                "Completado",
                                target.IPv4);
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            itemAllSucceeded[entry.SourceItem] = false;
                            itemLastError[entry.SourceItem] = $"{target.FullName}: {ex.Message}";
                            SetStatus($"Error enviando a {target.FullName}: {ex.Message}");

                            TransferHistoryStore.AppendEntry(
                                "ENVIADO",
                                entry.DisplayName,
                                entry.Size,
                                target.DisplayText,
                                "Error",
                                target.IPv4,
                                ex.Message);
                        }

                        sentBytesSoFar += entry.Size;
                        int completedPct = (int)Math.Round((double)sentBytesSoFar / grandTotalBytes * 100.0);
                        SetProgress(completedPct, completedPct >= 100 ? "listo" : _etaText);
                    }
                }
