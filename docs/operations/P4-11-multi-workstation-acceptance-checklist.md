# P4-11 — Recette multi-postes : checklist d'acceptation reproductible

> **Statut : PRÉPARÉE — NON EXÉCUTÉE.** Cette recette exige une infrastructure Windows réelle et un opérateur ; elle
> n'a **pas** été jouée. Aucune case ci-dessous n'est cochée par l'automatisation. Roadmap : §P4-11 (« parcours
> métier complets simultanés : comptoir + atelier + back-office ») ; elle porte aussi les preuves **Windows natives**
> renvoyées par les lots précédents (O12 : Q-P4-4-1, Q-P4-8-3, Q-P4-9-1 ; RR4 ; P4-7 ; Q-P4-10-3/4).
> Procédures appelées : [P4-8](P4-8-provisioning-procedure.md), [P4-9](P4-9-backup-restore-procedure.md),
> [P4-7](P4-7-sqlite-import-procedure.md).

Chaque étape : **action → résultat attendu → preuve à conserver** (capture, sortie de commande, fichier). Un écart
arrête la recette et ouvre une anomalie ; on ne « passe » jamais une étape non prouvée.

## 0. Prérequis (avant la journée de recette)

| # | Prérequis | Vérification |
|---|---|---|
| 0.1 | Laboratoire VirtualBox P4-1 Lot C : `MMV-SRV` (10.20.30.10) et `MMV-CLI` (10.20.30.20), réseau interne `MMV-LAB` | `VBoxManage list vms` ; ping croisé |
| 0.2 | **Licence d'évaluation Windows 11 Enterprise encore valide** sur chaque VM (installées vers le 26/07/2026 : une évaluation de 90 jours expire vers fin octobre 2026) | `slmgr /dlv` → date d'expiration postérieure à la recette ; sinon réarmer ou réinstaller **avant** |
| 0.3 | Un **second poste client** : clone lié de `MMV-CLI` → `MMV-CLI2` (10.20.30.21), nom d'ordinateur et SID distincts (`sysprep /generalize` ou clone + renommage) | deux clients + le serveur ; si impossible, `MMV-SRV` sert aussi de 3ᵉ poste (à consigner) |
| 0.4 | Instantané **`10-P4-11-START`** de chaque VM avant toute installation | liste des instantanés |
| 0.5 | Horloges synchronisées (NTP) sur les trois machines — exigence ADR-004 décision 9 | `w32tm /query /status` ; écart < 2 s entre machines |
| 0.6 | Paquets construits sur la station de développement depuis `p4-multi-poste` (SHA consigné) : `dotnet publish src/MMV.App -c Release -r win-x64 --self-contained -o <sortie>\MMV.App` et idem `src/MMV.DatabaseManager` | dossiers copiés sur les VM ; SHA du commit noté |
| 0.7 | Une **copie à froid d'une base SQLite de production réelle** (RR4), transportée hors réseau, et le fuseau du magasin | fichier + son SHA-256 |

## 1. Serveur (MMV-SRV) — installation native Windows (O12, Q-P4-8-3)

| # | Action | Attendu | Preuve |
|---|---|---|---|
| 1.1 | Installer PostgreSQL **17** natif (service Windows), certificat serveur TLS signé par l'autorité du magasin | service démarré, `ssl = on`, `password_encryption = scram-sha-256` | `psql -c "SHOW ssl"` |
| 1.2 | `provision` (procédure P4-8) | code 0 ; audit TLS/SCRAM/pg_hba conforme | sortie complète |
| 1.3 | `backup` → `verify-backup` → `migrate` (P4-9 §2) | 0 / 0 / 0 ; manifeste + preuve écrits | fichiers + sorties |
| 1.4 | `status` | migrations appliquées = connues ; métadonnée présente ; aucune maintenance | sortie |
| 1.5 | Pare-feu : 5432 ouvert au seul réseau du magasin ; refus depuis une autre plage | connexion refusée hors plage | capture |

## 2. Reprise des données SQLite (P4-7, RR4, Q-P4-4-1)

| # | Action | Attendu | Preuve |
|---|---|---|---|
| 2.1 | `backup` + `verify-backup` de la base migrée vide | 0 / 0 | manifeste |
| 2.2 | `import-sqlite … --dry-run` sur la copie de production | code 0 ; `RejectedValues` vide ; réconciliations lues et **acceptées par le métier** (écarts d'arrondi), `AmbiguousInstants` revus | `essai.json` signé par le responsable |
| 2.3 | `import-sqlite` réel avec le même manifeste, < 2 h | code 0 ; comptes par table = source | `import.json` |
| 2.4 | **Durée** de l'import et volumétrie réelles | consignées (aucune preuve de volumétrie réelle n'existe avant ce point) | chronométrage |
| 2.5 | Nouvelle `backup` + `verify-backup` | 0 / 0 | manifeste post-import |

Si 2.2 échoue : corriger la source avec MMV sur le poste d'origine, jamais à la main dans PostgreSQL.

## 3. Postes (MMV-CLI, MMV-CLI2)

| # | Action | Attendu | Preuve |
|---|---|---|---|
| 3.1 | `configure-workstation` sous l'utilisateur Windows qui lancera MMV (P4-8) | fichier DPAPI créé ; aucun secret en clair | capture du dossier |
| 3.2 | Démarrer MMV | contrôle D-15 + garde DP-4 passent ; écran de connexion | capture |
| 3.3 | Connexion avec un compte **importé** de SQLite | session ouverte, rôle correct | capture |
| 3.4 | Données importées visibles (clients, ventes, commandes, stock) et identiques à l'ancien poste | échantillon de 10 fiches comparé | tableau de comparaison |

## 4. Parcours métier simultanés (cœur de P4-11)

Trois opérateurs en même temps : **comptoir** (MMV-CLI), **atelier** (MMV-CLI2), **back-office** (MMV-SRV ou 3ᵉ poste).

| # | Scénario simultané | Attendu |
|---|---|---|
| 4.1 | Comptoir : client + ordonnance + vente avec monture ; atelier : consulte la commande dès sa création | la commande apparaît à l'atelier après actualisation |
| 4.2 | Deux postes vendent **le dernier exemplaire** d'un article dans la même minute | une vente réussit, l'autre reçoit un refus de stock clair ; stock 0 ; une seule vente |
| 4.3 | Atelier : génère la fiche, fabrique, contrôle qualité **réussi** ; comptoir : passe la commande à « Prête » | transition refusée avant QC, acceptée après |
| 4.4 | Deux postes règlent **le même solde** | un seul règlement |
| 4.5 | Back-office : crée un utilisateur `Marie` pendant qu'un autre poste crée `MARIE` | un seul compte, message de doublon sur l'autre |
| 4.6 | Back-office : supprime un fournisseur pendant qu'un poste crée un produit chez lui | jamais de produit orphelin ; message clair au perdant |
| 4.7 | Stock bas déclenché sur deux postes | une seule alerte active |
| 4.8 | Journée type : 30 min d'activité normale sur les trois postes | aucune erreur inattendue ; numéros de vente/commande continus et uniques |

## 5. Pannes réelles (critères 9, 10 ; Windows natif)

| # | Action | Attendu |
|---|---|---|
| 5.1 | Débrancher le câble réseau virtuel de MMV-CLI **pendant** l'enregistrement d'une vente | message « base inaccessible » ou « peut avoir été conservé — vérifiez » ; **jamais** de double vente après vérification ; rien de partiel |
| 5.2 | Rebrancher ; nouvelle opération sans redémarrer MMV | réussit |
| 5.3 | Redémarrer le **service** PostgreSQL pendant que les trois postes travaillent | opération en cours annulée proprement ; reprise sans redémarrer MMV |
| 5.4 | Arrêter le serveur ; démarrer MMV sur un poste | écran de blocage (base injoignable), quitter seulement ; aucun secret affiché |
| 5.5 | Tuer MMV (Gestionnaire des tâches) pendant une vente | rien de partiel ; l'article reste vendable par un autre poste |
| 5.6 | `migrate` d'une version suivante pendant que les postes travaillent (si une version N+1 existe) | postes N-1 continuent ; poste démarrant pendant la maintenance bloqué proprement |

## 6. Sauvegarde et exploitation (Q-P4-9-1, Q-P4-10-3)

| # | Action | Attendu |
|---|---|---|
| 6.1 | `configure-backup` sous le compte de tâche ; tâche planifiée quotidienne (P4-9 §1) déclenchée manuellement | code 0 ; fichiers sur le volume dédié **BitLocker** ; ACL NTFS conformes |
| 6.2 | Faire échouer la tâche (secret révoqué) | code ≠ 0 visible dans l'historique du Planificateur (alerte active : Q-P4-10-3) |
| 6.3 | `prune-backups` | rétention R-3 appliquée, rien supprimé à tort |
| 6.4 | Exercice de restauration après sinistre (P4-9 §5) dans une base neuve, `provision`, puis un poste s'y connecte | données identiques au manifeste |

## 7. Clôture de la recette

- Dossier de preuves (captures, sorties, rapports JSON, manifestes) archivé hors des VM, avec le SHA du code testé.
- Tableau des anomalies : aucune ouverte de sévérité bloquante.
- Signature du responsable métier sur §2.2 (écarts d'arrondi) et §4 (parcours).
- Restaurer les VM à `10-P4-11-START` si la recette doit être rejouée.

Ce n'est qu'avec ce dossier complet que **P4-11** peut être déclaré `COMPLETE`, et que **P4-12** (audit final) peut
statuer sur les critères 2, 3 et 18.
