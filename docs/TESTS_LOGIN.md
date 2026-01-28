# Tests Manuels - Écran de Login

## 📋 Checklist de Tests

### ✅ Tests Visuels

- [ ] **Layout Split-Screen**
  - Section gauche (marque) : fond blanc, logo, titre, description
  - Section droite (formulaire) : fond blanc, centré verticalement
  - Bordure grise entre les deux sections visible

- [ ] **Formulaire de Connexion**
  - [ ] Label "Identifiant" visible et lisible (police 14px, gras)
  - [ ] Champ Username : bordure grise, fond #F9F9FB, placeholder visible
  - [ ] Label "Mot de passe" visible et lisible
  - [ ] Champ Password : masqué avec des "•", placeholder visible
  - [ ] Bouton "Se connecter" : fond bleu (#0071E3), texte blanc, coins arrondis

- [ ] **Message d'Erreur**
  - [ ] Caché par défaut (aucune bordure rouge visible)
  - [ ] Apparaît après une tentative de connexion échouée
  - [ ] Fond rose (#FFF0F0), texte rouge, bordure rouge

- [ ] **Indicateur de Chargement**
  - [ ] Caché par défaut
  - [ ] Barre de progression bleue sous le bouton pendant le login
  - [ ] Disparaît après 500ms

- [ ] **Zone d'Information Démo**
  - [ ] Fond gris clair (#F5F5F7)
  - [ ] Texte "Utilisateur: admin | Mot de passe: admin" en police monospace

---

### ✅ Tests Fonctionnels

#### Test 1: État Initial
**Étapes:**
1. Lancer l'application
2. Observer l'écran de connexion

**Résultat Attendu:**
- Champs vides
- Bouton "Se connecter" **désactivé** (grisé)
- Aucun message d'erreur
- Aucun chargement

**Statut:** [ ]

---

#### Test 2: Validation des Champs Vides
**Étapes:**
1. Cliquer sur le bouton "Se connecter" (ne devrait rien faire)

**Résultat Attendu:**
- Bouton désactivé et non cliquable
- Aucune réaction

**Statut:** [ ]

---

#### Test 3: Validation Champ Username Seul
**Étapes:**
1. Saisir "admin" dans le champ Identifiant
2. Laisser le mot de passe vide
3. Observer le bouton

**Résultat Attendu:**
- Bouton **reste désactivé** (grisé)
- Impossible de cliquer

**Statut:** [ ]

---

#### Test 4: Validation Champ Password Seul
**Étapes:**
1. Vider le champ Identifiant
2. Saisir "admin" dans le champ Mot de passe
3. Observer le bouton

**Résultat Attendu:**
- Bouton **reste désactivé** (grisé)
- Impossible de cliquer

**Statut:** [ ]

---

#### Test 5: Activation du Bouton
**Étapes:**
1. Saisir "test" dans Identifiant
2. Saisir "test" dans Mot de passe
3. Observer le bouton

**Résultat Attendu:**
- Bouton **s'active** (bleu vif #0071E3)
- Curseur change en "pointer" au survol
- Texte "Se connecter" visible en blanc

**Statut:** [ ]

---

#### Test 6: Masquage du Mot de Passe
**Étapes:**
1. Saisir "test123" dans le champ Mot de passe
2. Observer le texte affiché

**Résultat Attendu:**
- Caractères masqués : "•••••••"
- Texte saisi non visible

**Statut:** [ ]

---

#### Test 7: Login Échoué (Mauvais Identifiants)
**Étapes:**
1. Saisir "wronguser" dans Identifiant
2. Saisir "wrongpass" dans Mot de passe
3. Cliquer sur "Se connecter"
4. Attendre 500ms

**Résultat Attendu:**
1. Bouton se désactive pendant le chargement
2. Barre de progression bleue apparaît
3. Après 500ms :
   - Message d'erreur rouge apparaît : "Nom d'utilisateur ou mot de passe incorrect."
   - Champs restent remplis
   - Bouton redevient actif (car champs remplis)

**Statut:** [ ]

---

#### Test 8: Login Réussi (Bons Identifiants)
**Étapes:**
1. Saisir "admin" dans Identifiant
2. Saisir "admin" dans Mot de passe
3. Cliquer sur "Se connecter"
4. Attendre 500ms

**Résultat Attendu:**
1. Bouton se désactive pendant le chargement
2. Barre de progression bleue apparaît
3. Après 500ms :
   - Fenêtre de login se **ferme**
   - Fenêtre principale **s'ouvre**
   - Dashboard visible avec 4 cartes colorées

**Statut:** [ ]

---

#### Test 9: Navigation Clavier (Entrée dans Username)
**Étapes:**
1. Saisir "admin" dans Identifiant
2. Appuyer sur **Entrée**

**Résultat Attendu:**
- Focus passe au champ Mot de passe
- Aucun login ne se déclenche

**Statut:** [ ]

---

#### Test 10: Navigation Clavier (Entrée dans Password)
**Étapes:**
1. Saisir "admin" dans Identifiant
2. Saisir "admin" dans Mot de passe
3. Appuyer sur **Entrée** (sans cliquer le bouton)

**Résultat Attendu:**
- Login se déclenche automatiquement
- Même comportement que le clic sur le bouton
- Fenêtre principale s'ouvre après 500ms

**Statut:** [ ]

---

#### Test 11: Hover sur Bouton Actif
**Étapes:**
1. Remplir les deux champs
2. Survoler le bouton avec la souris

**Résultat Attendu:**
- Couleur passe de #0071E3 (bleu) à #0056B3 (bleu foncé)
- Curseur change en "pointer"

**Statut:** [ ]

---

#### Test 12: Focus sur Champs de Texte
**Étapes:**
1. Cliquer dans le champ Identifiant
2. Observer la bordure
3. Cliquer dans le champ Mot de passe
4. Observer la bordure

**Résultat Attendu:**
- Bordure passe de grise (#D5D5D7) à bleue (#0071E3)
- Épaisseur passe de 1px à 2px
- Bordure disparaît quand on clique ailleurs

**Statut:** [ ]

---

#### Test 13: Effacement du Message d'Erreur
**Étapes:**
1. Provoquer une erreur (mauvais identifiants)
2. Message d'erreur s'affiche
3. Modifier le champ Username ou Password
4. Recliquer "Se connecter"

**Résultat Attendu:**
- Message d'erreur **disparaît** dès le clic sur le bouton
- Nouvelle tentative de connexion commence

**Statut:** [ ]

---

#### Test 14: Désactivation Pendant Chargement
**Étapes:**
1. Saisir "admin" / "admin"
2. Cliquer sur "Se connecter"
3. **Immédiatement** essayer de cliquer à nouveau

**Résultat Attendu:**
- Bouton **grisé** (#E5E5E5)
- Impossible de cliquer plusieurs fois
- Barre de progression visible

**Statut:** [ ]

---

## 📊 Résumé des Tests

| Catégorie | Tests Passés | Tests Total |
|-----------|--------------|-------------|
| Visuels | ___ / 5 | 5 |
| Fonctionnels | ___ / 14 | 14 |
| **TOTAL** | **___ / 19** | **19** |

---

## 🐛 Bugs Trouvés

| # | Description | Sévérité | Statut |
|---|-------------|----------|--------|
| 1 | | | |
| 2 | | | |
| 3 | | | |

---

## 📝 Notes

### Améliorations Possibles
- [ ] Animation de transition entre login et main window
- [ ] Bouton "Mot de passe oublié"
- [ ] Option "Se souvenir de moi"
- [ ] Meilleur feedback visuel pendant le chargement
- [ ] Animation sur le message d'erreur

### Points Positifs
- Interface propre et professionnelle
- Validation en temps réel
- Feedback immédiat (bouton actif/inactif)
- Tests unitaires couvrent tous les cas

---

**Date des tests:** _____________  
**Testeur:** _____________  
**Version:** Sprint 4  
**Environnement:** Windows 11 / .NET 8.0 / Avalonia 11.x
