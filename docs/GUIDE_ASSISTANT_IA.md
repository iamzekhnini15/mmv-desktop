# 💬 Assistant IA - Configuration et Utilisation

## 📋 Prérequis

### 1. Installer Ollama

Téléchargez et installez Ollama depuis [ollama.ai](https://ollama.ai/)

```bash
# Windows : télécharger l'installeur depuis ollama.ai
# Ou via winget
winget install Ollama.Ollama
```

### 2. Télécharger un modèle recommandé

Les modèles suivants sont testés et optimisés pour cette fonctionnalité :

```bash
# Option 1 : llama3.2 (RECOMMANDÉ - rapide et précis)
ollama pull llama3.2

# Option 2 : qwen2.5-coder:7b (très bon pour le SQL)
ollama pull qwen2.5-coder:7b

# Option 3 : deepseek-coder-v2 (excellent mais plus lourd)
ollama pull deepseek-coder-v2
```

### 3. Démarrer le serveur Ollama

```bash
# Le serveur démarre automatiquement sur Windows
# Vérifier qu'il tourne sur http://localhost:11434

# Test de connexion
curl http://localhost:11434/api/tags
```

---

## 🚀 Utilisation

### Lancer l'application

1. Dans l'application, cliquez sur **"💬 Chat IA"** dans la barre de navigation
2. Posez vos questions en français dans la zone de texte
3. L'IA génère le SQL et affiche les résultats automatiquement

### ✅ Exemples de questions

**Questions de comptage :**
- "Combien de clients ai-je ?"
- "Combien de produits dans mon stock ?"
- "Quel est le nombre total de ventes ?"

**Analyses de ventes :**
- "Top 10 des produits les plus vendus"
- "Ventes du mois de janvier 2026"
- "Chiffre d'affaires total pour février"
- "Liste des ventes supérieures à 500€"

**Gestion des stocks :**
- "Produits en stock bas"
- "Produits en rupture de stock"
- "Liste des fournisseurs actifs"

**CRM Clients :**
- "Clients ajoutés cette semaine"
- "Top 5 des meilleurs clients par montant"
- "Clients ayant une adresse email"

**Commandes :**
- "Commandes en attente"
- "Commandes livrées en janvier 2026"

---

## ⚙️ Configuration avancée

### Changer de modèle

Par défaut, l'application utilise **`llama3.2`**. Pour changer :

1. Ouvrez `LocalAiService.cs` (ligne 23)
2. Modifiez la constante :

```csharp
private const string DefaultModel = "qwen2.5-coder:7b"; // ou autre modèle
```

### Optimiser les performances

**Si les réponses sont trop lentes :**
- Utilisez `llama3.2` (le plus rapide)
- Réduisez `NumPredict` dans `LocalAiService.cs` (actuellement 200)
- Augmentez légèrement `Temperature` pour des réponses plus directes

**Si les réponses sont imprécises :**
- Passez à `qwen2.5-coder:7b` ou `sqlcoder`
- Augmentez `NumPredict` à 300-400
- Baissez `Temperature` à 0.1

### Paramètres dans le code

```csharp
// LocalAiService.cs - ligne 53
Options = new OllamaOptions
{
    Temperature = 0.2,    // 0.0-1.0 : créativité (plus bas = plus précis)
    NumPredict = 200,     // Nombre max de tokens générés
    TopP = 0.9           // Nucleus sampling (0.9 recommandé)
}
```

---

## 🔒 Sécurité

L'exécuteur SQL (`SqlExecutorService`) applique les protections suivantes :

✅ **Lecture seule** : Seules les requêtes `SELECT` et `WITH` sont autorisées  
✅ **Validation stricte** : Blocage de `INSERT`, `UPDATE`, `DELETE`, `DROP`, etc.  
✅ **Protection injection** : Détection de requêtes multiples (`;`)  
✅ **Limite automatique** : Maximum 100 lignes par requête

---

## 🐛 Dépannage

### Erreur "Impossible de contacter Ollama"

```bash
# Vérifier que le serveur est démarré
curl http://localhost:11434/api/tags

# Redémarrer Ollama
# Windows : Redémarrer le service depuis les paramètres système
```

### L'IA génère du SQL invalide

- Essayez de reformuler la question plus simplement
- Vérifiez que le modèle est bien téléchargé : `ollama list`
- Passez à un modèle plus spécialisé comme `sqlcoder`

### Les réponses sont trop longues

- Réduisez `NumPredict` à 150 dans `LocalAiService.cs`
- Ajoutez "Liste les 10 premiers" dans votre question

### Le DataGrid n'affiche rien

- Vérifiez que votre requête retourne des résultats
- Consultez le SQL généré affiché dans l'interface
- Testez le SQL manuellement via un outil SQLite

---

## 📊 Base de données

L'IA a accès aux tables suivantes :

- **Customers** : clients et coordonnées
- **Products** : catalogue produits avec stock
- **Sales / SaleItems** : ventes et détails
- **Orders / OrderItems** : commandes atelier
- **Prescriptions** : ordonnances ophtalmologiques
- **Suppliers** : fournisseurs
- **Users** : utilisateurs du système
- **StockMovements** : mouvements de stock
- **Notifications** : notifications système

---

## 🎯 Bonnes pratiques

1. **Soyez précis** : "Produits en stock bas" plutôt que "stock"
2. **Spécifiez les limites** : "Top 10" plutôt que "produits populaires"
3. **Dates explicites** : "Ventes de janvier 2026" plutôt que "ventes récentes"
4. **Questions simples** : Une métrique à la fois pour plus de précision

---

## 📝 Notes techniques

- **Modèle par défaut** : `llama3.2` (ligne 23 de `LocalAiService.cs`)
- **Schéma BD** : Simplifié pour optimiser les performances (ligne 89)
- **Prompt système** : Contient des exemples "few-shot" (ligne 106)
- **Timeout** : 3 minutes maximum par requête

Pour plus d'informations, consultez le code source dans :
- `Services/LocalAiService.cs`
- `Services/SqlExecutorService.cs`
- `ViewModels/ChatDataViewModel.cs`
