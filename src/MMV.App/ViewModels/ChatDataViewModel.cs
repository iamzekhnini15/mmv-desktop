using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Dynamic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using MMV.App.Commands;
using MMV.App.Services;

namespace MMV.App.ViewModels;

public class ChatDataViewModel : BaseViewModel
{
    private readonly LocalAiService _aiService;
    private readonly SqlExecutorService _sqlExecutor;

    private string _userInput = string.Empty;
    private CancellationTokenSource? _currentCts;

    // Historique complet (Texte + Données)
    public ObservableCollection<ChatMessage> ChatHistory { get; } = new();

    public string UserInput
    {
        get => _userInput;
        set
        {
            if (SetProperty(ref _userInput, value))
            {
                (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CancelCommand { get; }

    public ChatDataViewModel(LocalAiService aiService, SqlExecutorService sqlExecutor)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _sqlExecutor = sqlExecutor ?? throw new ArgumentNullException(nameof(sqlExecutor));

        Title = "Assistant IA";

        SendCommand = new RelayCommand(async () => await SendMessageAsync(), () => CanSend());
        ClearCommand = new RelayCommand(ClearChat);
        CancelCommand = new RelayCommand(CancelCurrentRequest, () => IsLoading);
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(UserInput) && !IsLoading;

    private async Task SendMessageAsync()
    {
        var question = UserInput.Trim();
        if (string.IsNullOrWhiteSpace(question)) return;

        // 1. Ajouter le message utilisateur
        ChatHistory.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = question
        });

        UserInput = string.Empty;
        IsLoading = true;
        (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        
        _currentCts = new CancellationTokenSource();

        // 2. Ajouter un indicateur "typing" (3 points animés)
        var typingMessage = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            IsTyping = true
        };
        ChatHistory.Add(typingMessage);

        try
        {
            // 3. Générer le SQL
            var sql = await _aiService.GenerateSqlQueryAsync(question, cancellationToken: _currentCts.Token);

            if (string.IsNullOrWhiteSpace(sql))
            {
                typingMessage.IsTyping = false;
                typingMessage.Content = "Je n'ai pas réussi à comprendre la demande.";
                return;
            }

            // 4. Exécuter le SQL
            DataTable dataTable = await _sqlExecutor.ExecuteQueryAsync(sql, _currentCts.Token);

            // 5. Convertir DataTable en liste dynamique
            var resultsList = new List<dynamic>();
            if (dataTable != null)
            {
                foreach (DataRow row in dataTable.Rows)
                {
                    dynamic dynObj = new ExpandoObject();
                    var dict = dynObj as IDictionary<string, object>;
                    foreach (DataColumn col in dataTable.Columns)
                    {
                        dict[col.ColumnName] = row[col] == DBNull.Value ? "N/A" : row[col];
                    }
                    resultsList.Add(dynObj);
                }
            }

            // 6. Générer la réponse textuelle avec STREAMING
            var rowCount = resultsList.Count;

            if (rowCount == 0)
            {
                typingMessage.IsTyping = false;
                typingMessage.Content = "Aucun résultat trouvé pour cette requête. 🤷";
                typingMessage.SqlQuery = sql;
            }
            else
            {
                // Préparer les données pour l'IA
                var sampledData = resultsList.Take(10).ToList();
                var jsonData = JsonSerializer.Serialize(sampledData, new JsonSerializerOptions 
                { 
                    WriteIndented = false,
                    MaxDepth = 3
                });

                try
                {
                    // STREAMING : Les mots apparaissent un par un
                    await _aiService.StreamTextResponseAsync(
                        question,
                        sql,
                        $"{rowCount} ligne(s). Échantillon: {jsonData}",
                        token =>
                        {
                            // IMPORTANT : Utiliser le UI thread pour mettre à jour l'interface
                            Dispatcher.UIThread.Post(() =>
                            {
                                typingMessage.IsTyping = false; // Enlever les 3 points
                                typingMessage.Content += token; // Ajouter le token au message
                            });
                        },
                        cancellationToken: _currentCts.Token
                    );

                    // Ajouter les données et le SQL
                    typingMessage.SqlQuery = sql;
                    typingMessage.Data = resultsList;
                }
                catch
                {
                    typingMessage.IsTyping = false;
                    typingMessage.Content = rowCount == 1
                        ? "✅ J'ai trouvé 1 résultat."
                        : $"✅ J'ai trouvé {rowCount} résultats.";
                    typingMessage.SqlQuery = sql;
                    typingMessage.Data = resultsList;
                }
            }
        }
        catch (OperationCanceledException)
        {
            typingMessage.IsTyping = false;
            typingMessage.Content = "Annulé.";
        }
        catch (Exception ex)
        {
            typingMessage.IsTyping = false;
            typingMessage.Content = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _currentCts?.Dispose();
            _currentCts = null;
            (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void AddAssistantMessage(string content, string? sql)
    {
        ChatHistory.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = content,
            SqlQuery = sql
        });
    }

    private void ClearChat()
    {
        ChatHistory.Clear();
    }

    private void CancelCurrentRequest()
    {
        _currentCts?.Cancel();
    }
}