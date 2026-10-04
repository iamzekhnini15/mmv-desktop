# MMV — Dossier de transmission vers Claude Design (UI-PREP)

> **Nature du document.** Traduction factuelle du dépôt réel (branche `p3-business-rules`) en contraintes et
> capacités utiles au designer. Aucune maquette, aucun jugement esthétique. Le **code prime** sur les documents
> produit : chaque statut ci-dessous est adossé à un fichier du dépôt.
>
> **Statuts utilisés** : `EXISTE` · `PARTIEL` · `MANQUANT` · `HORS_V1` · `V1_PLUS` · `DECISION_REQUISE`.
>
> **Baseline vérifiée** : build Debug vert (0 avertissement) · 751 tests verts (App 239 · Application 218 ·
> Domain 294) · aucun code, test ou migration modifié par ce travail.

---

## 1. Produit et utilisateurs

**MMV — ManageMyVision.** Logiciel de gestion pour un magasin d'optique unique au Maroc.

| Élément | Cible V1 | État dépôt |
|---|---|---|
| Plateformes | Desktop Windows + macOS | Avalonia / .NET 8 — multiplateforme par construction (`src/MMV.App`) |
| Langue | Français | `EXISTE` — toute l'UI et tous les messages métier sont en français |
| Devise | MAD | `DECISION_REQUISE` — les montants sont des `decimal` **sans devise ni formatage localisé** ; aucun symbole MAD dans le code |
| Déploiement | 1 magasin, 1 base centrale, plusieurs postes | `PARTIEL` — base SQLite locale ; la stratégie multi-poste est **cadrée mais non implémentée** (`docs/architecture/adr-prod-db-001-multi-poste-database-strategy.md`) |
| Thèmes | Clair + sombre | `EXISTE` — `IThemeService` + bascule dans Paramètres |
| Référence 1920×1080, clavier/souris | — | Cohérent avec l'existant (aucune adaptation tactile) |
| Identité | Nouvelle, médicale, rassurante | À produire — l'existant utilise des **emoji** comme icônes (`📊 👥 📦 📋 🏭 💰 🔔 📈 ⚙️ 🔐`), à remplacer intégralement |

### Rôles

| Rôle cible produit | Existe en base ? |
|---|---|
| Propriétaire | `MANQUANT` — approché par `Admin` |
| Responsable / Opticien | `EXISTE` — `Optician` |
| Vendeur | `MANQUANT` |
| Technicien atelier | `EXISTE` — `Technician` |
| Accueil / Réception | `MANQUANT` |

`src/MMV.Domain/Enums/UserRole.cs` ne contient que **3 valeurs** : `Admin`, `Optician`, `Technician`. Un
utilisateur porte un seul rôle (`User.Role`), ce qui est conforme à la cible. Passer à 5 rôles est un
changement de domaine + migration (voir §5 et §6).

### Parcours prioritaire

`Client → Ordonnance → Vente → Commande / Atelier → Récapitulatif`

Le parcours **existe de bout en bout** mais il est **entièrement imbriqué dans le module Clients** : on
sélectionne un client, puis on saisit l'ordonnance, puis on lance la vente depuis la fiche client. La vente
crée automatiquement la commande atelier si elle contient des verres. Il n'y a **pas** de point d'entrée
« Nouvelle vente » autonome (voir §3, écrans `Ventes` et `Ordonnances` = pages vides).

---

## 2. Carte des modules

| Module | État | Écrans existants | Actions réellement supportées | Rôles (contrôle réel) | Limites principales |
|---|---|---|---|---|---|
| **Authentification** | `EXISTE` | `LoginView` | Connexion par identifiant/mot de passe hashé ; déconnexion ; expiration de session après **30 min d'inactivité** | Tous | Pas de « mot de passe oublié », pas de verrouillage écran, pas de multi-session |
| **Accueil (Tableau de bord)** | `PARTIEL` | `DashboardView` | Affiche 4 compteurs + 3 raccourcis | Tous | ⚠️ **Les chiffres sont codés en dur** (`42`, `156`, `12540.50`, `8`) — `DashboardViewModel.LoadData()` contient un `// TODO`. **Aucune donnée réelle.** Accueil identique pour tous les rôles |
| **Clients** | `EXISTE` | `CustomersView`, `CustomerFormView`, `CustomerDetailView` (+ onglets Infos / Ordonnances / Historique d'achats) | Créer, modifier, **archiver / réactiver**, supprimer (protégée), lister avec pagination, rechercher, filtre « inclure archivés » | Backend : aucun. UI : Technicien masqué en écriture | Recherche = filtre **en mémoire** sur la page chargée, pas une requête serveur |
| **Ordonnances** | `EXISTE` (dans Clients) | `PrescriptionFormView`, `PrescriptionDetailView`, `CustomerPrescriptionsView` | Créer, modifier, supprimer, consulter — **toujours rattachées à un client** | Backend : aucun. UI : Technicien masqué en écriture | L'entrée de menu « Ordonnances » ouvre une **page vide** (voir §3). Pas d'archivage d'ordonnance. Pas d'écart pupillaire (EP/DP) dans l'entité |
| **Ventes** | `PARTIEL` | `SaleFormView` (accessible **uniquement** depuis la fiche client) | Panier, remise, acompte, méthode de paiement, enregistrement transactionnel | Backend : aucun. UI : Technicien n'accède pas au module | L'entrée de menu « Ventes » ouvre une **page vide**. Vente non modifiable ni annulable après enregistrement |
| **Paiements** | `PARTIEL` | intégré à `SaleFormView` et `OrderDetailView` | Acompte à la vente + **encaissement du solde** sur la commande ; méthodes Espèces / Carte / Chèque / Virement | Backend : aucun | Un seul encaissement de solde possible (met le restant à 0 d'un coup) ; **aucun historique de paiements** (pas d'entité `Payment`) ; **aucun remboursement / annulation** |
| **Commandes** | `EXISTE` | `OrdersView`, `OrderFormView`, `OrderDetailView` | Créer, modifier, supprimer, faire avancer le statut, encaisser le solde | Backend : aucun | Les transitions de statut sont **libres** (saut d'étape et retour arrière possibles) |
| **Atelier** | `PARTIEL` | `OrderKanbanView` (6 colonnes), `FabricationSheetView` | Kanban par statut ; fiche de fabrication affichée à l'écran | Backend : aucun | La fiche atelier est **éphémère** : non persistée, non versionnée, **non imprimable** (le bouton « Imprimer » ne fait qu'afficher la vue) |
| **Contrôle qualité** | `MANQUANT` | — | — | — | `OrderStatus.QualityCheck` **existe comme statut**, mais aucune checklist, aucun résultat de contrôle, aucun blocage |
| **Produits** | `EXISTE` | `ProductsView`, `ProductFormView`, `ProductDetailView`, `ProductOrderHistoryView` | Créer, modifier, supprimer, consulter, historique des commandes du produit ; détails Verre / Lentille / Accessoire | Backend : aucun | Pas d'unicité de référence vérifiée ; pas d'import |
| **Stock** | `EXISTE` | `InventoryView`, `StockMovementsView`, `StockMovementFormView` | Mouvements Entrée / Sortie / Ajustement ; inventaire avec comptage et ajustement confirmé ; décrément **atomique** (jamais négatif) | Backend : aucun | Le décrément lié à la fabrication contourne le service de mutation (risque de stock négatif — connu, planifié P3-5) |
| **Fournisseurs** | `EXISTE` | `SuppliersView`, `SupplierFormView`, `SupplierDetailView` | Créer, modifier, supprimer, consulter avec produits liés | Backend : aucun | Aucun validateur : email/téléphone non validés |
| **Utilisateurs** | `EXISTE` | `UsersView`, `UsersListView`, `UserFormView`, `UserProfileView` | Créer, modifier, activer / désactiver | UI : Admin seulement | Aucune suppression ; 3 rôles seulement |
| **Notifications** | `PARTIEL` | `NotificationsView` | Lister, compter les non-lues, tout marquer comme lu ; génération automatique « stock bas » | Tous | **Un seul type réellement produit** (`LowStock`) ; pas de résolution automatique quand le stock remonte |
| **Rendez-vous** | `MANQUANT` | — | — | — | Aucune entité, aucun use case, aucun écran |
| **Recherche globale** | `MANQUANT` | — | — | — | Il n'existe que des recherches locales, module par module |
| **Brouillons** | `MANQUANT` | — | — | — | `SaleStatus.Draft` existe comme valeur d'enum mais **aucune vente n'est jamais enregistrée en brouillon** |
| **Paramètres magasin** | `MANQUANT` | `SettingsView` | Bascule thème clair/sombre + version de l'app | Tous sauf Technicien (UI) | Aucun paramètre de magasin (raison sociale, adresse, TVA, logo, devise, horaires) |
| **Impressions** | `MANQUANT` | — | — | — | Aucun code d'impression, d'export PDF ou de génération de document dans tout le dépôt |
| **Rapports** | `MANQUANT` | `ReportsView` | — | Tous sauf Technicien (UI) | Écran **placeholder** : « Module en cours de développement » |

---

## 3. Carte des écrans existants

### 3.1 Écrans réels

**`LoginView` — `LoginViewModel`**
Connexion. Appelle `IAuthenticationService`. États : identifiants invalides, compte inactif, erreur technique.
*À conserver conceptuellement* : écran plein, un seul champ d'identité + mot de passe.
*Entièrement remplaçable* : toute la présentation.

**`DashboardView` — `DashboardViewModel`**
4 tuiles + 3 raccourcis (Vente, Client, Inventaire).
⚠️ **Défaut fonctionnel majeur** : les valeurs sont **simulées en dur**, aucun use case n'est appelé.
*À conserver* : l'idée d'un accueil orienté action.
*Remplaçable* : tout. Le designer doit considérer l'accueil comme **à concevoir depuis zéro**, y compris la
question des accueils différenciés par rôle (non implémentée).

**`CustomersView` — `CustomersViewModel` / `CustomersListViewModel`**
Liste paginée, recherche texte, case « inclure les archivés ».
Use cases : `IListCustomersUseCase`, `IDeleteCustomerUseCase`, `ISetCustomerArchivedUseCase`.
États métier : client introuvable (« Le client visé est introuvable. ») ; **suppression refusée** lorsque le
client a un historique (ventes/ordonnances) — l'UI propose alors l'**archivage** comme alternative sûre.
*À conserver* : le couple **archiver / réactiver** et le filtre d'inclusion des archivés — c'est une vraie
règle métier, pas un habillage.
*Remplaçable* : la mise en page de la liste, la pagination visuelle, la barre de recherche.

**`CustomerDetailView` — `CustomerDetailViewModel`**
Conteneur à onglets : `CustomerInfoView`, `CustomerPrescriptionsView`, `CustomerPurchaseHistoryView`.
C'est le **hub réel du parcours métier** : c'est de là que partent la création d'ordonnance et la vente.
*À conserver conceptuellement* : la centralité de la fiche client.
*Remplaçable* : la forme « onglets ».

**`CustomerFormView` — `CustomerFormViewModel`**
Création/édition. Validation : prénom et nom requis (≤100), email valide si renseigné, téléphone ≤20,
n° sécurité sociale ≤30. Erreurs renvoyées **par champ** (`ValidationError(PropertyName, Message)`).

**`PrescriptionFormView` — `PrescriptionFormViewModel`**
Saisie OD/OG : sphère, cylindre, axe, addition, prisme (valeur + base), acuité visuelle, médecin, date, notes.
Use cases : `ICreatePrescriptionUseCase`, `IUpdatePrescriptionUseCase`.
**Règles métier réellement appliquées** (`PrescriptionValidator`, P3-3B) :
- date d'émission ≤ aujourd'hui + 1 jour ;
- sphère ∈ [−20, 20] · cylindre ∈ [−6, 6] · addition ∈ [0, 4] · axe ∈ [0, 180] ;
- **cylindre non nul ⇒ axe obligatoire** ; **axe renseigné sans cylindre ⇒ refusé** ;
- **prisme > 0 ⇒ base obligatoire** ; **base sans valeur de prisme ⇒ refusé** ; prisme négatif refusé ;
- création/modification **refusée pour un client archivé**.
*À conserver* : ces règles et leur affichage **sous le champ concerné** — la couche UI reçoit déjà les erreurs
propriété par propriété.
*Défaut à connaître* : **pas de champ écart pupillaire (EP/DP)** dans l'entité `Prescription`, alors qu'un
opticien l'attend. Aucun champ de validité/expiration d'ordonnance.

**`SaleFormView` — `SaleFormViewModel`**
Panier (recherche produit, filtre catégorie, quantité), remise, **acompte**, méthode de paiement, notes,
bascule « vente comptoir ». Suggère les verres compatibles à partir de l'ordonnance active du client.
Use cases : `IGetSaleFormReferenceDataUseCase`, `IRegisterSaleUseCase`.
Comportement backend : vente comptoir ⇒ statut `Delivered` + sortie de stock ; vente avec verres ⇒
`AwaitingLenses` + **création automatique d'une commande atelier**. Le tout est **atomique**.
*Défauts à connaître* : les montants envoyés par l'UI ne sont **pas revalidés** côté métier (acompte supérieur
au total techniquement acceptable — connu, planifié P3-7) ; **une vente enregistrée n'est ni modifiable ni
annulable**.

**`OrdersView` / `OrdersListViewModel`, `OrderDetailView`, `OrderFormView`**
Liste, détail, édition, suppression (confirmée), avancement de statut, **encaissement du solde**.
Use cases : `IListOrdersUseCase`, `IGetOrderDetailsUseCase`, `IAdvanceOrderStatusUseCase`,
`ISettleOrderBalanceUseCase`, `ICreateOrderUseCase`, `IUpdateOrderUseCase`, `IDeleteOrderUseCase`.
États métier : « Commande introuvable. » ; passage `À fabriquer → En fabrication` déclenche des mouvements de
stock et une notification.
*Défaut à connaître* : **aucune règle de transition** — n'importe quel statut peut succéder à n'importe quel
autre. Une maquette qui suggère un chemin contraint décrirait une cible, pas l'existant.

**`OrderKanbanView` — `OrderKanbanViewModel`**
6 colonnes : Nouvelle · À fabriquer · En fabrication · Contrôle qualité · Prête · Livrée.
*À conserver* : le Kanban et ces 6 statuts — ils correspondent exactement à `OrderStatus`.

**`FabricationSheetView` — `FabricationSheetViewModel`**
Fiche atelier : n° commande, dates, client (nom + téléphone), monture, verre OD, verre OG, accessoires, notes.
*Défaut à connaître* : **elle n'imprime pas**. Le bouton « Imprimer la fiche » affiche simplement la vue à
l'écran. Elle n'est ni persistée ni versionnée.
*À conserver* : le **découpage OD / OG / monture / accessoires**, qui est la structure de travail réelle.

**Produits / Stock / Fournisseurs / Utilisateurs / Notifications**
CRUD complets et fonctionnels, conformes au tableau du §2.

### 3.2 Écrans qui n'existent pas (mais figurent au menu)

Trois entrées du menu latéral ouvrent une **page de texte statique** « en cours de développement » :

| Entrée de menu | Fichier | Contenu réel |
|---|---|---|
| **Ordonnances** | `Views/PrescriptionsView.axaml` | Titre + « Module en cours de développement… » |
| **Ventes** (Point de vente) | `Views/SalesView.axaml` | Titre + « Module en cours de développement… » |
| **Rapports** | `Views/ReportsView.axaml` | Titre + « Module en cours de développement… » |

C'est le **piège principal** du dépôt : le menu donne l'impression de 11 modules, il n'y en a que 8 utilisables.
Ordonnances et Ventes **fonctionnent réellement**, mais uniquement **depuis la fiche client**.

---

## 4. Contrats UI importants

Ce que les maquettes doivent savoir représenter, tel que le backend le produit **aujourd'hui**.

| Contrat | État | Forme réelle |
|---|---|---|
| **Validation de champ** | `EXISTE` | Les use cases d'écriture renvoient `IReadOnlyList<ValidationError>` où `ValidationError = (PropertyName, Message)`. Le message est **déjà en français et prêt à afficher**. Rien n'est écrit en base si la liste est non vide. ⇒ **L'erreur se place sous le champ.** |
| **Entité introuvable** | `EXISTE` | Convention homogène : le résultat porte un booléen `*Found` (`CustomerFound`, `OrderFound`, `PrescriptionFound`…). `false` ⇒ **aucune écriture**, l'UI affiche un message court (« Commande introuvable. »). ⇒ **bandeau ou message d'état, pas une erreur bloquante.** |
| **Refus métier** | `EXISTE` | Deux refus réels aujourd'hui : (1) **suppression d'un client avec historique** → l'UI propose l'archivage ; (2) **ordonnance sur client archivé** → refusée. ⇒ Un refus métier doit être **explicatif et proposer l'alternative**, pas juste barrer le bouton. |
| **Erreur technique** | `PARTIEL` | `BaseViewModel.ErrorMessage` (string unique) + `IDialogService.ShowErrorAsync(titre, message)`. Pas de code d'erreur, pas de distinction technique/métier au niveau du type. ⇒ Prévoir **une zone d'erreur par écran** et **une modale d'erreur**. |
| **Chargement** | `EXISTE` | `BaseViewModel.IsLoading` (booléen), plus des indicateurs dédiés (`IsSaving`, `IsLoadingProducts`). Contrôle `LoadingSpinner` existant. ⇒ Prévoir un état de chargement **global à l'écran** et des états **locaux à une zone** (ex. liste produits du panier). |
| **Liste vide** | `MANQUANT` | Aucun écran n'a d'état vide dédié ; une liste vide s'affiche simplement vide. ⇒ **À concevoir** pour chaque liste. |
| **Confirmation** | `EXISTE` | `IDialogService.ShowConfirmationAsync(titre, message)` → booléen. Utilisée pour : suppression client, suppression ordonnance, suppression produit, suppression commande, suppression fournisseur, ajustement d'inventaire. ⇒ **Modale binaire.** Rien d'autre ne demande confirmation aujourd'hui. |
| **Rechargement multi-poste** | `MANQUANT` | Chaque écran a un `RefreshCommand` **manuel**. Aucune détection de changement distant, aucun rafraîchissement automatique. |
| **Dernier rafraîchissement** | `MANQUANT` | Aucun horodatage de fraîcheur n'est exposé par les ViewModels. Un badge « mis à jour à HH:MM » serait une **cible**, pas un rendu de l'existant. |
| **Conflits de concurrence** | `MANQUANT` | Aucun jeton de concurrence (`RowVersion`/`ETag`) sur les entités. Le dernier écrit gagne, silencieusement. Le multi-poste est **cadré** dans l'ADR mais **non implémenté**. ⇒ **Ne pas maquetter d'écran de résolution de conflit comme existant.** |
| **Session expirée** | `EXISTE` | Après 30 min d'inactivité, `SessionService` vide la session et l'app **retourne à l'écran de connexion**. ⇒ État à représenter. |

---

## 5. Matrice des rôles réelle

⚠️ **Point le plus important de ce dossier.**

Une recherche exhaustive de `UserRole` / `IPermissionService` dans `src/MMV.Application` et `src/MMV.Domain`
ne renvoie **aucun contrôle de permission**. Les seules occurrences sont des champs de données (`User.Role`,
`CreateUserCommand.Role`, `UserListItemDto.Role`).

**Conséquence : il n'existe aujourd'hui aucune permission contrôlée par le backend.**
`PermissionService` vit dans la couche UI (`src/MMV.App/Services/PermissionService.cs`) et ne fait que **piloter
l'affichage** : masquer une entrée de menu, désactiver un bouton. Un utilisateur Technicien dont l'UI masque
« Ventes » pourrait déclencher `RegisterSaleUseCase` sans être arrêté par quoi que ce soit.

| Capacité | Contrôle backend | Visibilité UI seule | Cible produit non implémentée |
|---|---|---|---|
| Accès aux modules (Ventes, Rapports, Paramètres, Utilisateurs) | ❌ aucun | ✅ `PermissionService.CanAccessModule` | — |
| Créer / modifier client, produit, ordonnance, commande, vente | ❌ aucun | ✅ Technicien exclu de l'écriture | — |
| Supprimer client / produit / commande / vente | ❌ aucun (seule la **protection d'historique** bloque, indépendamment du rôle) | ✅ Admin seulement | — |
| Gérer les utilisateurs | ❌ aucun | ✅ Admin seulement | — |
| Rembourser (`CanRefund`) | ❌ **la fonctionnalité elle-même n'existe pas** | ✅ Admin seulement | Remboursement (§6) |
| Changer le statut d'une commande | ❌ aucun | ✅ tous les rôles authentifiés | — |
| Rôles Propriétaire / Vendeur / Accueil | — | — | ⛔ **absents de l'enum** — 3 rôles seulement |

**À transmettre au designer :** ne jamais présenter le masquage d'un bouton comme une garantie de sécurité. La
matrice de rôles à 5 entrées est une **cible produit**, à maquetter comme telle, et elle suppose côté backend
un élargissement de l'enum + une migration + une autorisation réellement appliquée dans les use cases.

---

## 6. Écarts backend bloquant certaines maquettes

Chaque ligne est adossée à une preuve dans le dépôt.

| # | Capacité | État | Module | Preuve | Maquettable comme cible ? | Bloque l'implémentation ? | Phase backend recommandée |
|---|---|---|---|---|---|---|---|
| 1 | **Rendez-vous** | `MANQUANT` | Rendez-vous | Aucune entité, aucun use case, aucun écran | ✅ oui | ✅ oui — tout est à créer (entité + migration + use cases + écrans) | Nouvelle phase P4 |
| 2 | **Brouillons centraux** | `MANQUANT` | Ventes / transverse | `SaleStatus.Draft` existe mais `RegisterSaleUseCase` ne pose jamais ce statut ; aucune reprise de brouillon | ✅ oui | ✅ oui | P3-7 (Ventes) |
| 3 | **Recherche globale** | `MANQUANT` | Transverse | Recherches locales en mémoire uniquement (`CustomersListViewModel.ApplyFilter`, `InventoryViewModel`) | ✅ oui | ✅ oui — nécessite un use case de recherche transverse | Nouvelle phase P4 |
| 4 | **Préférences de tableaux par utilisateur** | `MANQUANT` | Transverse | Aucune entité de préférence, aucune persistance de colonnes/tri | ✅ oui | ✅ oui | Nouvelle phase P4 |
| 5 | **Paramètres magasin** | `MANQUANT` | Paramètres | `SettingsViewModel` = thème + version, rien d'autre | ✅ oui | ✅ oui — bloque aussi les impressions (en-tête magasin) | Nouvelle phase P4 |
| 6 | **Rôles détaillés (5 rôles)** | `PARTIEL` | Utilisateurs | `UserRole` = 3 valeurs ; aucune autorisation backend | ✅ oui | ✅ oui | P3-10 (Utilisateurs) |
| 7 | **Autorisation réellement appliquée** | `MANQUANT` | Transverse | Aucun `UserRole` dans Application/Domain | ✅ oui | ✅ oui — enjeu de **sécurité**, pas de design | P3-10 |
| 8 | **Audit des ventes** | `MANQUANT` | Ventes | `Sale.StaffId` existe (qui a vendu) mais aucune trace de modification, aucun journal | ✅ oui | ✅ oui | P3-7 |
| 9 | **Annulation / remboursement tracé** | `MANQUANT` | Ventes / Paiements | `SaleStatus.Cancelled` et `PaymentStatus.Refunded` existent **comme valeurs d'enum jamais posées** ; aucun use case | ✅ oui | ✅ oui | P3-7 |
| 10 | **Historique de paiements (acompte + solde)** | `PARTIEL` | Paiements | `Sale.DepositAmount` / `RemainingAmount` (2 champs) + `SettleOrderBalanceUseCase` qui solde **en une fois** ; aucune entité `Payment` | ✅ oui | ⚠️ partiellement — l'acompte et le solde final fonctionnent ; les **paiements multiples** n'existent pas | P3-7 |
| 11 | **Contrôle qualité persisté** | `MANQUANT` | Atelier | `OrderStatus.QualityCheck` = simple étape ; pas de checklist, pas de résultat, pas de blocage | ✅ oui | ✅ oui | P3-6B (déjà planifié) |
| 12 | **Fiche atelier persistée / versionnée** | `PARTIEL` | Atelier | `FabricationSheetViewModel` = vue éphémère dérivée de `GetOrderDetails` | ✅ oui | ⚠️ l'écran existe, la persistance non | P3-6B (déjà planifié) |
| 13 | **Impression A5 / reçu interne / ticket client** | `MANQUANT` | Impressions | **Aucun code d'impression ou de PDF dans tout le dépôt** ; le bouton « Imprimer la fiche » ne fait qu'afficher une vue | ✅ oui | ✅ oui — dépend aussi de l'écart n° 5 | Nouvelle phase P4 |
| 14 | **Export CSV** | `MANQUANT` | Transverse | Aucun code d'export | ✅ oui | ✅ oui | Nouvelle phase P4 |
| 15 | **Tableau de bord réel** | `MANQUANT` | Accueil | Valeurs codées en dur + `// TODO` | ✅ oui | ✅ oui — nécessite des use cases d'agrégation | Nouvelle phase P4 |
| 16 | **Transitions de statut contraintes** | `MANQUANT` | Commandes | `AdvanceOrderStatusUseCase` accepte n'importe quel statut suivant | ✅ oui | ⚠️ non — l'écran existe, la règle manque | P3-6 (déjà planifié) |
| 17 | **Concurrence multi-poste** | `MANQUANT` | Transverse | Aucun jeton de concurrence ; ADR de cadrage seulement | ✅ oui | ✅ oui | P3-5 / P3-6 / P3-7 |
| 18 | **Devise MAD / formatage monétaire** | `DECISION_REQUISE` | Transverse | `decimal` nus, aucun symbole ni culture | ✅ oui | ⚠️ non bloquant, mais à trancher tôt | Transverse |

---

## 7. Contraintes à transmettre à Claude Design

Décisions produit confirmées, à respecter dans toute proposition :

**Shell et navigation**
- Barre latérale **rétractable** (candidate) — remplace le menu fixe actuel à 11 entrées ;
- **Recherche globale en haut** — à concevoir comme cible (le backend ne la sert pas encore, voir §6) ;
- **Accueil distinct selon le rôle** — à concevoir (l'accueil actuel est unique et factice) ;
- **Barre d'état** en bas ;
- **Pas d'onglets multi-documents en V1** ;
- Parcours par **écrans séparés avec indicateur de progression** (pas d'assistant modal monolithique) ;
- **Tableaux** pour les listes denses, **cartes** pour le Kanban et les vues de synthèse.

**Domaine optique**
- **OD / OG adaptatifs** (un œil peut être vide) ;
- Ordre des champs optiques : **Sphère → Cylindre → Axe → Addition → Prisme (valeur puis base) → Acuité** ;
- Bases prismatiques — libellés imposés :
  - **Nasal = In** · **Temporal = Out** · **Supérieur = Up** · **Inférieur = Down**
  - (correspond exactement à `PrismBase { In, Out, Up, Down }`) ;
- Contraintes de saisie déjà appliquées par le métier, à refléter visuellement : sphère [−20, 20], cylindre
  [−6, 6], addition [0, 4], axe [0, 180] ; **cylindre ⇒ axe** ; **prisme ⇒ base**.

**Formulaires et retours**
- Formulaires **guidés** ;
- **Erreurs sous les champs** (le backend fournit déjà `PropertyName` + message français) ;
- **Confirmations uniquement pour les actions sensibles** (suppressions, ajustement d'inventaire — pas ailleurs).

**Atelier**
- **Kanban** à 6 colonnes, alignées sur `OrderStatus` : Nouvelle · À fabriquer · En fabrication · Contrôle
  qualité · Prête · Livrée ;
- **Checklist qualité** — à maquetter en cible (n'existe pas) ;
- **Impression A5** — à maquetter en cible (n'existe pas).

**Identité**
- Modes **clair et sombre** obligatoires (l'infrastructure de thème existe déjà) ;
- **Aucun symbole d'œil, de lunettes ou d'optique** dans l'identité ;
- Remplacer **tous** les emoji actuellement utilisés comme icônes.

---

## 8. Fonctions à ne pas présenter comme existantes

Toute maquette illustrant l'une de ces fonctions doit porter l'étiquette **`FONCTION V1 À DÉVELOPPER`**.

1. **Tableau de bord avec des chiffres réels** — les valeurs actuelles sont codées en dur.
2. **Recherche globale.**
3. **Rendez-vous** (agenda, planning, rappels).
4. **Brouillons** (vente ou tout autre document repris plus tard).
5. **Préférences de tableaux par utilisateur** (colonnes, tri, densité mémorisés).
6. **Paramètres du magasin** (raison sociale, adresse, logo, devise, TVA, horaires).
7. **Toute impression** : reçu interne, ticket client, fiche atelier A5, PDF. *Le bouton « Imprimer la fiche »
   existant n'imprime rien.*
8. **Export CSV.**
9. **Annulation de vente et remboursement** (même tracés).
10. **Paiements multiples / échelonnés** — seuls un acompte + un solde unique existent.
11. **Checklist de contrôle qualité** et blocage du passage sans QC.
12. **Fiche atelier persistée / versionnée / historisée.**
13. **Rôles Propriétaire, Vendeur, Accueil** — seuls Admin, Opticien, Technicien existent.
14. **Sécurité par rôle** — aucune permission n'est appliquée côté backend.
15. **Rafraîchissement automatique multi-poste, indicateur de fraîcheur, résolution de conflit.**
16. **Module Rapports.**
17. **Écrans « Ordonnances » et « Ventes » autonomes** depuis le menu — ces parcours ne sont accessibles que
    depuis la fiche client.
18. **Tablette Android atelier** — `V1_PLUS`, hors périmètre V1.

---

## 9. Données fictives de démonstration

Jeu cohérent, sans donnée personnelle réelle. Numéros de téléphone en plage fictive, aucun numéro de sécurité
sociale réaliste.

**Clients**

| Nom | Naissance | Téléphone | Ville | État |
|---|---|---|---|---|
| Yasmine El Amrani | 1988-03-14 | 06 00 00 12 01 | Casablanca | actif |
| Omar Benali | 1975-11-02 | 06 00 00 12 02 | Rabat | actif |
| Salma Bouhaddou | 1994-07-21 | 06 00 00 12 03 | Marrakech | actif |
| Rachid Tazi | 1962-01-09 | 06 00 00 12 04 | Fès | actif |
| Nadia Cherkaoui | 2001-05-30 | 06 00 00 12 05 | Tanger | **archivée** |

**Ordonnances** (respectent les règles du validateur)

| Client | Date | Médecin | OD | OG |
|---|---|---|---|---|
| Yasmine El Amrani | 2026-05-12 | Dr. Alaoui | Sph −2.25 · Cyl −0.75 · Axe 175 | Sph −2.00 · Cyl −0.50 · Axe 10 |
| Omar Benali | 2026-06-03 | Dr. Sekkat | Sph +1.50 · Add 2.00 | Sph +1.75 · Add 2.00 |
| Rachid Tazi | 2026-02-20 | Dr. Bennani | Sph −4.00 · Prisme 2.0 base **In** | Sph −3.75 |
| Salma Bouhaddou | 2026-06-28 | Dr. Alaoui | Sph −1.00 | Sph −1.25 · Cyl −0.50 · Axe 90 |

**Produits**

| Référence | Nom | Catégorie | Prix vente | Stock | Seuil |
|---|---|---|---|---|---|
| MON-0148 | Monture Atlas Titane | MONTURE | 890,00 | 12 | 3 |
| MON-0212 | Monture Zellige Acétate | MONTURE | 640,00 | 4 | 5 ⚠️ |
| VER-0301 | Verre unifocal 1.60 antireflet | VERRE | 450,00 | — | — |
| VER-0355 | Verre progressif 1.67 | VERRE | 1 350,00 | — | — |
| LEN-0410 | Lentilles mensuelles ×6 | LENTILLE | 320,00 | 22 | 6 |
| SOL-0503 | Solaire Sahara polarisée | SOLAIRE | 720,00 | 8 | 3 |
| ACC-0601 | Étui rigide + microfibre | CLIPS | 60,00 | 40 | 10 |

**Ventes**

| N° | Client | Total | Remise | Final | Acompte | Restant | Paiement | Statut |
|---|---|---|---|---|---|---|---|---|
| VTE-000118 | Yasmine El Amrani | 1 790,00 | 90,00 | 1 700,00 | 700,00 | 1 000,00 | Espèces | En attente de verres |
| VTE-000119 | Salma Bouhaddou | 380,00 | 0,00 | 380,00 | 380,00 | 0,00 | Carte | Livrée (comptoir) |
| VTE-000120 | Omar Benali | 2 240,00 | 140,00 | 2 100,00 | 1 000,00 | 1 100,00 | Carte | En fabrication |

**Commandes / travaux atelier**

| N° | Vente | Client | Statut | Livraison estimée |
|---|---|---|---|---|
| CMD-000091 | VTE-000118 | Yasmine El Amrani | À fabriquer | 2026-07-21 |
| CMD-000092 | VTE-000120 | Omar Benali | En fabrication | 2026-07-19 |
| CMD-000093 | — | Rachid Tazi | Contrôle qualité | 2026-07-16 |
| CMD-000094 | — | Salma Bouhaddou | Prête | 2026-07-15 |

**Rendez-vous** *(données de cible — aucune structure ne les porte aujourd'hui)*

| Client | Date | Motif |
|---|---|---|
| Nadia Cherkaoui | 2026-07-18 09:30 | Essayage monture |
| Omar Benali | 2026-07-19 15:00 | Retrait commande |

**Notifications**

| Type | Message |
|---|---|
| LowStock | « Stock bas : Monture Zellige Acétate (4 restants, seuil 5) » |
| *(cible)* OrderReady | « CMD-000094 prête pour retrait — Salma Bouhaddou » |

---

## 10. Prompt de départ Claude Design

```
Tu conçois l'identité et le socle de design de MMV — ManageMyVision : un logiciel desktop
(Windows + macOS) de gestion pour un magasin d'optique unique au Maroc. Interface en français,
devise MAD, référence 1920×1080, clavier et souris, modes clair et sombre obligatoires.

Utilisateurs : Propriétaire, Responsable/Opticien, Vendeur, Technicien atelier, Accueil.
Un utilisateur possède un seul rôle. Parcours principal : Client → Ordonnance → Vente →
Commande/Atelier → Récapitulatif.

Ton : médical, rassurant, moderne, technologique. Contrainte impérative : aucun symbole d'œil,
de lunettes ou d'optique dans l'identité.

À cette première étape, produis UNIQUEMENT :

1. Trois directions d'identité visuelle distinctes (nom de la direction, principe, palette,
   typographie, traitement des formes et de l'iconographie, ce que chacune dit du produit).

2. Un design system clair/sombre pour la direction que tu recommandes : jetons de couleur
   (surfaces, texte, bordures, accent, sémantique succès/avertissement/erreur/information),
   échelle typographique, échelle d'espacement, rayons, élévations, et l'état de chaque
   composant de base (bouton, champ, champ en erreur, table, carte, badge de statut, modale,
   indicateur de chargement, état vide).

3. Trois propositions de shell/navigation, chacune intégrant : une barre latérale rétractable,
   une recherche globale en haut, un accueil différencié par rôle, une barre d'état en bas.
   Pas d'onglets multi-documents.

Ne produis AUCUNE maquette d'écran métier à cette étape.
```

---

## 11. Récapitulatif

- **19 modules recensés** — 8 réellement utilisables, 3 entrées de menu vides, 8 inexistants.
- **Écart de sécurité principal** : aucune permission n'est appliquée côté backend ; le rôle ne fait que
  masquer des boutons.
- **Écart fonctionnel principal** : ni impression, ni export, ni rendez-vous, ni recherche globale, ni
  paramètres magasin, ni annulation/remboursement, ni contrôle qualité — et un tableau de bord factice.
- **Acquis solides** à préserver conceptuellement : archivage client avec protection d'historique, validation
  optique croisée (cylindre⇒axe, prisme⇒base), vente transactionnelle avec création automatique de la commande
  atelier, décrément de stock atomique, Kanban à 6 statuts, découpage OD/OG/monture/accessoires de la fiche
  atelier.
