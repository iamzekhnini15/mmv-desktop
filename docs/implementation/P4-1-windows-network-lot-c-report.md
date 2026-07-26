# MMV — P4-1 · Lot C — Rapport de porte de disponibilité (environnement lab Windows + réseau)

> **Portée de ce document — état courant.** Ce document couvre **l'ensemble du Lot C** : la **porte de
> disponibilité** du laboratoire (§1–§16) **puis son exécution effective** (§17–§20). Le **verdict en vigueur**
> est celui de **§20** : `P4-1 LOT C = PASS` (pistes PostgreSQL **et** SQL Server toutes deux `PASS`). Le
> **prochain lot bloquant**, `P4-1 Lot D`, est défini en **§21**. **Aucun provider n'est choisi ni éliminé ;
> aucune ADR P4-2 n'est créée ; `P4-1 SPIKE` reste `INCOMPLETE`.**
>
> **Portée initiale — historique (§1–§14).** À leur date, ces sections étaient **uniquement la porte de
> disponibilité** (readiness gate) du Lot C : aucune installation, aucune modification de pare-feu, aucun
> redémarrage, aucun service créé, aucune configuration TCP modifiée, aucun choix de provider, aucune ADR — rien
> n'avait alors été installé ni modifié. Elles constatent l'état, énumèrent les prérequis manquants, proposent
> la procédure exacte, et s'arrêtent. **Le contenu évidentiel de §1–§14 est préservé : aucune preuve d'origine
> n'y a été supprimée ni réécrite en silence.** La reconciliation du 2026-07-26 y a seulement **ajouté** une
> annotation explicite de supersession (bandeau + étiquette `[HISTORIQUE]`) sur le verdict de **§14**, dont le
> contenu constaté reste par ailleurs celui de l'époque.
>
> **Mise à jour 2026-07-25.** Un laboratoire conforme aux prérequis §12 a été provisionné entre la première
> évaluation (§1–§14, **historique**) et cette date. Voir **§15–§16** pour la revalidation de la porte à partir
> du laboratoire réellement provisionné. §1–§14 restent la trace, **contenu évidentiel préservé**, de la
> **première** tentative (`BLOCKED`) ; seule une **annotation de supersession** a été ajoutée en §14.
>
> **Mise à jour 2026-07-26.** Le Lot C a ensuite été **exécuté** dans ce laboratoire : voir **§17–§18** pour la
> piste **PostgreSQL** et **§19–§20** pour la piste **SQL Server Express** et la clôture du Lot C ; **§21**
> définit le prochain lot bloquant (**Lot D**, portabilité applicative). Les verdicts intermédiaires de §14,
> §16 et §18 sont **historiques** et **remplacés** par celui de **§20** : **leur contenu évidentiel est
> préservé** — aucune preuve antérieure n'a été supprimée ni réécrite en silence — et seuls des **bandeaux de
> supersession explicites** (« Verdict remplacé », étiquettes `[HISTORIQUE]`) ont été **ajoutés** en tête de
> chacun.

---

## 1. Paramètres et base autoritaire

| Élément | Valeur constatée |
|---|---|
| Branche | `p4-multi-poste` |
| HEAD local | `f9df67ac8753812fbb5dd22cc147a69ef19200d3` |
| HEAD distant (`origin/p4-multi-poste`) | `f9df67ac8753812fbb5dd22cc147a69ef19200d3` |
| `origin/main` | `3ed883634dae83399d3e93dbc1dc65ffaabdf243` |
| Arbre de travail **à la base `f9df67a`** (avant les édits de reconciliation documentaire du Lot C) | propre (suivis) — seuls `design-handoff/`, `design/`, `docs/ui/` non suivis |
| `git diff --check` | propre |
| CI de référence | run `30129393484` |
| CI — `event` / `status` / `conclusion` | `push` / `completed` / **`success`** |
| CI — SHA de tête | `f9df67ac8753812fbb5dd22cc147a69ef19200d3` (exact) |
| CI — job unique *Restore / Build / Test / Scan* | ✅ tous les steps verts (Restore · Build · Test · audit vulnérabilités · contrôle EF) |
| Base MMV | 1499 tests (Domain 649 · Application 611 · App 239) — baseline P4-1 |
| P4-1 Lot A / Lot B | GO / GO |
| P4-1 SPIKE | INCOMPLETE |
| P4-2 ADR | BLOCKED |
| Provider | **aucun choisi, aucun éliminé** |

> **Note — état courant du dépôt (2026-07-26, reconciliation documentaire).** La ligne « Arbre de travail »
> ci-dessus décrit l'état **à la base `f9df67a`**, avant l'édition des documents Lot C. Depuis cette base, la
> reconciliation documentaire du Lot C a modifié **`docs/architecture/P4-multi-poste-roadmap.md`** (fichier
> **suivi**, désormais `M` dans `git status`) et créé **ce rapport lui-même** en tant que fichier **non suivi**
> supplémentaire (`docs/implementation/P4-1-windows-network-lot-c-report.md`, `??`). **L'arbre de travail suivi
> n'est donc plus propre au moment de la lecture de ce document** — cet état est attendu, documentaire
> uniquement, ne touche ni le code ni les tests, et **aucun commit n'a été effectué**.
>
> **Note.** Le `EXPECTED_HEAD = 1f54f36…` du prompt de spike d'origine est **historique** et n'est **pas**
> utilisé ici. La base autoritaire est `f9df67a…` (CI `30129393484` verte).

---

## 2. Rappel — pourquoi le Lot C existe

D'après la roadmap P4 et le rapport de spike P4-1, tous les critères du spike sont couverts **sauf** :

- **installation Windows native** des deux providers (PostgreSQL et SQL Server **Express**) depuis un état Windows **propre** ;
- **accès réseau réel depuis un second poste** (multi-poste) ;
- **snapshot / rollback** d'un environnement jetable pour rejouer proprement.

Le Lot C exige donc un **laboratoire à deux machines Windows** isolé des données de production/personnelles.
La mémoire du projet note aussi qu'une **tentative antérieure d'installation native de SQL Server Express a
échoué** (`0x84C4000E`, cause racine non déterminée) — ce qui renforce l'obligation d'un état Windows propre
et jetable, et interdit d'improviser sur la station de développement.

---

## 3. Prérequis Lot C — évaluation

| # | Prérequis exigé | Constat | Verdict |
|---|---|---|---|
| 1 | Une VM serveur Windows **propre** ou machine lab dédiée | Aucune VM Windows. Seuls des invités **Linux** WSL (`Ubuntu`, `docker-desktop`). `Get-VM` (module Hyper-V) indisponible. Pas de CLI VirtualBox/VMware. | ❌ NON DISPONIBLE |
| 2 | Une seconde VM/machine Windows **client** | Aucun second point de terminaison Windows détecté. | ❌ NON DISPONIBLE |
| 3 | Connectivité **réseau privé** entre les deux | Impossible : il n'existe pas deux machines à relier. Adaptateur host-only VirtualBox (`192.168.56.1/24`) et Radmin VPN présents sur l'unique hôte, sans pair lab Windows. | ❌ IMPOSSIBLE À ÉTABLIR |
| 4 | Droits **administrateur** sur la VM serveur | Aucune VM serveur. De plus le shell courant **n'est pas élevé** (`IsElevatedShell = False` ; `Get-WindowsOptionalFeature` a échoué : *nécessite une élévation*). | ❌ N/A + shell non élevé |
| 5 | Capacité de **snapshot / rollback** | Windows 11 **Famille (Home)** ne fournit pas la plateforme de gestion Hyper-V (`Get-VM` absent). Pas de CLI VirtualBox/VMware. `HypervisorPresent=True` reflète seulement l'usage du *Windows Hypervisor Platform* par WSL2/Docker — sans snapshot d'invité **Windows**. | ❌ NON CONFIRMÉE |
| 6 | **Aucune** donnée de production/personnelle | La station exécute déjà un service **natif `postgresql-x64-17`** (Automatic, Running) et des composants **SQL Server** (`SQLWriter` Running ; outils client SQLCMD 170 + ODBC installés). Données potentiellement personnelles/dev. | ❌ VIOLÉ si cette station est utilisée |
| 7 | Espace disque **suffisant** | C: 28 Go libres / 931 (bas) · **D: 1065 Go libres** / 3726 · **Y: 2794 Go libres** / 2795. Capacité suffisante existe — mais sans objet tant que la station ne peut **pas** être le lab. | ⚠️ Capacité OK, mais non pertinente ici |
| 8 | Tester PostgreSQL **et** SQL Server Express **séparément depuis un état Windows propre** | Le seul état Windows disponible est la station de dev **non propre** (PostgreSQL 17 natif + composants SQL Server déjà installés ; échec natif Express `0x84C4000E` antérieur). Aucun socle Windows propre à installer/annuler. | ❌ IMPOSSIBLE |

**Garde-fou explicite** — *« la station de développement ne doit pas servir de lab destructif »* :
**CONFIRMÉ**. Cette station héberge le dépôt, exécute des services DB candidats à la production avec données
potentiellement personnelles, est en Windows **Home** (sans snapshot), et a déjà subi un échec d'installation
Express. Elle **ne doit pas** être réutilisée comme laboratoire Lot C.

---

## 4. Machines disponibles

| Machine | Rôle constaté | Détail |
|---|---|---|
| `DESKTOP-BPS3B6T` | **Station de développement** (unique machine présente) | ASUS · AMD Ryzen 7 5800X (8c/16t) · 32 Go RAM · virtualisation firmware activée |

**Aucune** seconde machine Windows (physique ou VM) n'est présente. Les seuls « invités » sont des VM
**Linux** WSL (`Ubuntu`, `docker-desktop`), inaptes à jouer un serveur/poste **Windows**.

---

## 5. Versions Windows

| Élément | Valeur |
|---|---|
| Édition | **Windows 11 Famille (Home)** — `ProductName` legacy = « Windows 10 Home », `EditionID = Core` |
| Version / Build | `10.0.26200` / build `26200` |
| Architecture | 64 bits |
| Domaine | `WORKGROUP` (hors domaine) |

> **Conséquence dure.** L'édition **Home** ne propose pas la plateforme **Hyper-V** de gestion des VM Windows
> (création + snapshot). C'est un bloqueur direct des prérequis #1, #2 et #5 via la pile Microsoft intégrée.

---

## 6. Rôles serveur / client

Non attribuables : il n'existe qu'une seule machine, qui est la station de développement et ne peut pas être
le lab. **Aucun** rôle serveur ni client n'est disponible.

---

## 7. Configuration réseau (topologie sur l'hôte unique)

Adaptateurs actifs constatés sur `DESKTOP-BPS3B6T` (aucun pair lab Windows) :

| Interface | IP (privée / lien-local) | Nature |
|---|---|---|
| Wi-Fi | `192.168.0.34/24` | LAN domestique |
| Ethernet 2 (VirtualBox Host-Only) | `192.168.56.1/24` | Adaptateur résiduel host-only, sans invité |
| vEthernet (WSL) | `172.24.176.1/20` | Réseau WSL/Hyper-V interne (Linux) |
| Radmin VPN | `26.25.255.30/8` | VPN (aucun pair lab confirmé) |
| iptv-tunnel (WireGuard) | `10.8.0.2/24` | Tunnel tiers, hors périmètre lab |
| divers | `169.254.*` | APIPA / lien-local (non routés) |

Il n'existe **aucun réseau privé point-à-point entre deux machines Windows**, car la seconde machine n'existe pas.

---

## 8. Droits administrateur

- Shell courant : **non élevé** (`IsElevatedShell = False`, utilisateur `DESKTOP-BPS3B6T\Ali Zekhnini`).
- `Get-WindowsOptionalFeature -Online` a échoué : *« L'opération demandée nécessite une élévation. »*
- Aucune VM serveur sur laquelle des droits admin puissent être vérifiés.

---

## 9. Snapshot / rollback

Non confirmé. Aucun mécanisme de snapshot d'invité **Windows** disponible : Hyper-V de gestion absent (Home),
pas de CLI VirtualBox (`VBoxManage` introuvable) ni VMware (`vmrun` introuvable). WSL ne fournit pas de
snapshot d'invité Windows.

---

## 10. Espace disque

| Volume | Libre (arrondi) | Taille (arrondie) |
|---|---|---|
| C: | ~28 Go | ~931 Go |
| D: | ~1065 Go | ~3726 Go |
| Y: | ~2794 Go | ~2795 Go |

Capacité disque **suffisante** existe (D:, Y:) pour héberger de futures VM — mais cet hôte ne peut pas être le
lab destructif. C: est bas (~28 Go) et ne doit pas être ciblé.

---

## 11. Services / outils DB déjà présents sur la station (constat, non utilisés)

| Élément | État | Remarque |
|---|---|---|
| Service `postgresql-x64-17` | Running · Automatic | Installation **native** PostgreSQL 17 sur la station de dev — **non inspectée, non touchée** |
| Service `SQLWriter` | Running · Automatic | Composant SQL Server VSS Writer présent |
| `sqlcmd` (SQLCMD 170) + ODBC 170 | Installé | Outils client SQL Server présents |
| `psql` / `pg_dump` | Introuvables sur le PATH | Client PostgreSQL non exposé |
| `docker`, `wsl` | Présents | Docker Desktop + WSL2 (invités Linux uniquement) |
| `VBoxManage`, `vmrun` | Introuvables | Pas d'hyperviseur de bureau opérable en CLI |
| `dotnet` | 8.0.417 | — |

> Ces éléments **ne sont ni utilisés ni modifiés** par ce rapport. Leur seule présence **disqualifie** la
> station comme « état Windows propre » et comme environnement « sans données personnelles ».

---

## 12. Prérequis manquants (liste exacte)

1. Une **VM serveur Windows propre** ou une **machine lab Windows dédiée** (physique ou virtuelle).
2. Une **seconde machine/VM Windows cliente**.
3. Un **réseau privé** reliant serveur et client (host-only, réseau interne, ou LAN isolé).
4. **Droits administrateur** sur la machine serveur du lab.
5. Une **capacité de snapshot / rollback** de l'état Windows serveur (Hyper-V Pro/Enterprise, VirtualBox, VMware, ou clonage disque).
6. Une **édition Windows** permettant la virtualisation d'invités Windows **ou** une seconde machine physique (l'hôte actuel est Windows **Home**).
7. Un **socle Windows propre**, sans PostgreSQL/SQL Server préinstallés, pour installer chaque provider **séparément** et pouvoir revenir en arrière.
8. **Aucune donnée** de production/personnelle sur le lab.

---

## 13. Procédure de test proposée (à exécuter APRÈS approbation et une fois le lab disponible)

> Aucune de ces étapes n'est exécutée maintenant. Elles constituent le plan à valider.

**A. Préparation du lab (avant toute installation)**
1. Provisionner **VM-SRV** (Windows, propre) et **VM-CLI** (Windows, propre) sur un hôte doté de snapshot.
2. Configurer un **réseau privé** entre VM-SRV et VM-CLI ; noter les adresses (sous-réseau dédié).
3. Prendre un **snapshot « clean »** de VM-SRV et VM-CLI avant tout logiciel.
4. Confirmer **droits admin** sur VM-SRV ; confirmer **absence de toute donnée** MMV réelle.

**B. Piste PostgreSQL (isolée)**
5. Restaurer VM-SRV au snapshot « clean ».
6. Installer **PostgreSQL** (version notée par requête serveur), service Windows, écoute TCP, utilisateur dédié jetable.
7. Ouvrir le **port** dans le pare-feu **uniquement** pour le sous-réseau privé.
8. Depuis **VM-CLI**, ouvrir une connexion TCP réelle, relever la version, exécuter les expériences E1–E10 sur une **base jetable**.
9. Tester **sauvegarde/restauration** avec les outils officiels ; tester une **coupure réseau** simulée.
10. Snapshot d'après-installation, puis **rollback** au « clean ».

**C. Piste SQL Server Express (isolée)**
11. Restaurer VM-SRV au snapshot « clean » (état sans PostgreSQL).
12. Installer **SQL Server Express** (édition/version relevées par requête serveur) — surveiller le mode d'échec `0x84C4000E` déjà rencontré ; capturer les logs d'installation.
13. Activer **TCP/IP** + navigateur SQL si nécessaire, ouvrir le **port** au seul sous-réseau privé.
14. Depuis **VM-CLI**, connexion TCP réelle, version/édition serveur, expériences E1–E10 sur base jetable.
15. Sauvegarde/restauration officielle ; coupure réseau simulée ; **rollback**.

**D. Consolidation**
16. Remplir la matrice comparative (niveaux de preuve `EXECUTED` / `OFFICIAL_DOCUMENTATION` / `ENGINE_ONLY` / `NOT_TESTED` / `BLOCKED`).
17. **Aucun choix** de provider ; mettre à jour le rapport de spike et la roadmap avec les preuves factuelles.

**Points d'arrêt obligatoires** (STOP + approbation) avant : installer PostgreSQL · installer SQL Server
Express · modifier le pare-feu Windows · redémarrer une machine · créer un service · modifier la config TCP.

---

## 14. Verdict *(historique — remplacé)*

> **Verdict §14 remplacé.** Ce `BLOCKED` correspond à la **première** tentative de porte de disponibilité, à une
> date où **aucun laboratoire n'existait**. Il a été **levé** par §16 (`READY TO EXECUTE`) puis définitivement
> **remplacé** par le verdict en vigueur de **§20** (`P4-1 LOT C = PASS`). Il est conservé tel quel comme trace
> historique et **ne décrit plus l'état courant**.

```
P4-1 LOT C = BLOCKED — LAB ENVIRONMENT NOT AVAILABLE          [HISTORIQUE — remplacé par §20]
```

**Motifs** : aucune seconde machine Windows ; aucune VM Windows (seulement des invités Linux WSL) ; pas de
capacité de snapshot d'invité Windows (hôte Windows **Home**, pas de CLI VirtualBox/VMware) ; la seule machine
disponible est la **station de développement**, non propre (PostgreSQL 17 natif + composants SQL Server, données
potentiellement personnelles) et **interdite** comme lab destructif.

**Prochaine action minimale** : fournir/provisionner un environnement lab conforme (§12), puis relancer cette
porte de disponibilité. Tant que la porte n'est pas franchie, **aucune** installation, **aucun** changement
réseau/pare-feu, **aucun** redémarrage n'est entrepris.

**Contraintes respectées** : aucun provider choisi ou éliminé ; aucune ADR P4-2 ; aucune modification de
`src/**`, `tests/**`, migrations, snapshot EF, UI, `.github/**`, `MMV.sln`, packages de production ; aucun
commit ; aucun push ; aucune installation ; aucune modification de pare-feu ; aucun redémarrage ; aucune action
destructive. Aucune donnée utilisateur utilisée.

---

*Rapport de porte de disponibilité — Lot C, première tentative. Établi sur la base `f9df67a` / CI `30129393484`
(verte). Fin de cette première tentative de gate ; arrêt pour décision sur l'environnement. **Voir §15–§16 pour
la revalidation du 2026-07-25.***

---

## 15. Revalidation — laboratoire provisionné (2026-07-25)

> **Portée de cette section.** Revalidation de la seule **porte de disponibilité** du Lot C, sur la base d'un
> laboratoire à deux VM Windows réellement provisionné depuis la clôture de §1–§14. **Aucune** installation de
> provider, **aucune** modification de pare-feu/TCP/service/configuration VM n'est effectuée par cette
> revalidation ; **aucun** provider n'est choisi ou éliminé ; **aucune** ADR P4-2. Base Git/CI reconfirmée
> identique à §1 (`f9df67a…` / CI `30129393484`, `conclusion=success`, SHA exact) — voir §16.

### 15.1 Machines et hyperviseur

Le blocage de §5/§12 (« Windows **Home** ne fournit pas Hyper-V de gestion, aucune VM Windows possible ») est
**levé** : le laboratoire n'utilise **pas** Hyper-V mais **Oracle VirtualBox 7.2.8** (r173730), qui s'exécute
au-dessus de Windows 11 Home sans dépendre de la plateforme de gestion Hyper-V.

Vérifié indépendamment (`VBoxManage showvminfo`, lecture seule, aucune modification) :

| Machine | Rôle | Type d'OS invité (VirtualBox) | Édition déclarée (opérateur) | État VM au moment du constat |
|---|---|---|---|---|
| `MMV-SRV` | Serveur | `Windows 11 (64-bit)` — build guest `10.0.26200.6584` (`GuestInfo/OS/Product = Windows 11`) | Windows 11 **Enterprise Evaluation** | `running` (déjà en cours ; non démarrée par cette session) |
| `MMV-CLI` | Client | `Windows 11 (64-bit)` | Windows 11 **Enterprise Evaluation** | `poweroff` (non redémarrée par cette session) |

Chaque VM est une **machine VirtualBox distincte** (UUID propre : `d8e132a0-…` pour `MMV-SRV`,
`ca606843-…` pour `MMV-CLI`), sur des dossiers de stockage séparés (`D:\VMs\MMV-Lab\MMV-SRV`,
`Y:\VMs\MMV-LAB\MMV-CLI`). L'édition précise (« Enterprise Evaluation ») n'est pas exposée telle quelle par
`GuestInfo/OS/Product` (qui ne renvoie que `Windows 11`) ; elle est **déclarée par l'opérateur** ayant réalisé
l'installation et **non re-vérifiée** par une commande d'édition (`DISM`/`slmgr`) depuis cette session.

### 15.2 Configuration réseau

Confirmée indépendamment via `VBoxManage showvminfo --machinereadable` (lecture seule) :

| Machine | Adaptateur 1 | Adaptateur 2 | MAC Adaptateur 1 | MAC Adaptateur 2 |
|---|---|---|---|---|
| `MMV-SRV` | `nat` (`natnet1=nat`) | `intnet` réseau interne **`MMV-LAB`** | `08:00:27:18:12:C6` | `08:00:27:95:4D:3E` |
| `MMV-CLI` | `nat` (`natnet1=nat`) | `intnet` réseau interne **`MMV-LAB`** | `08:00:27:F9:87:7B` | `08:00:27:61:08:1D` |

Les **quatre adresses MAC sont distinctes** (préfixe `08:00:27` = OUI standard VirtualBox, attendu). Les deux
adaptateurs 2 partagent le même réseau interne nommé `MMV-LAB`, ce qui les relie l'un à l'autre en isolation du
LAN de l'hôte et d'Internet (l'adaptateur 1, NAT, gère uniquement la sortie Internet indépendante par VM).

Adresses IP privées sur l'adaptateur interne — **confirmées pour `MMV-SRV`** par lecture directe de la
propriété invité (`GuestInfo/Net/1/V4/IP = 10.20.30.10`, VM en cours d'exécution au moment du constat) ; pour
`MMV-CLI` (VM arrêtée au moment du constat, donc non interrogeable en direct), l'adresse est **attestée par la
description du snapshot `01-LAB-READY`** (§15.5) plutôt que re-vérifiée en direct :

| Machine | IP privée déclarée | Masque | Passerelle | DNS |
|---|---|---|---|---|
| `MMV-SRV` | `10.20.30.10` (confirmé en direct) | `/24` | **aucune** sur l'adaptateur interne | **aucun** sur l'adaptateur interne |
| `MMV-CLI` | `10.20.30.20` (attesté par snapshot) | `/24` | **aucune** sur l'adaptateur interne | **aucun** sur l'adaptateur interne |

L'absence de passerelle/DNS sur l'adaptateur interne n'est pas vérifiable à distance sans élévation dans
l'invité ; elle est prise pour acquise d'après la description de snapshot et la configuration déclarée par
l'opérateur, **non re-vérifiée** par une commande exécutée à l'intérieur des invités par cette session.

Classification réseau **Privé** des adaptateurs internes : **attestée par la description du snapshot**
(« Profil privé »), **non re-vérifiée** par une commande `Get-NetConnectionProfile` à l'intérieur des invités
par cette session (aucun accès shell aux invités depuis cette session).

### 15.3 Pare-feu Windows et règles ICMP

D'après la description du snapshot `01-LAB-READY` (§15.5, identique sur les deux VM) : **Pare-feu Windows
demeure activé** sur les deux machines ; des **règles ICMP** ont été ajoutées, **restreintes** à l'adresse de
l'autre VM sur le réseau interne, et à l'interface **« Ethernet 2 »** (l'adaptateur interne `MMV-LAB`) — pas
une autorisation ICMP globale.

**Non re-vérifié** par cette session : le contenu exact des règles de pare-feu (`Get-NetFirewallRule`) n'a pas
été inspecté à l'intérieur des invités, faute d'accès shell aux VM depuis cette revalidation. Ce point est
**attesté par l'opérateur** (description de snapshot), pas re-exécuté.

### 15.4 Connectivité réseau réelle

D'après la description du snapshot `01-LAB-READY` : **ping `MMV-SRV` → `MMV-CLI` réussi** et **ping `MMV-CLI`
→ `MMV-SRV` réussi**, en **bidirectionnel**, sur le réseau interne `MMV-LAB`. Ceci lève le blocage §3/§7
(« impossible d'établir une connectivité réseau privé entre deux machines : il n'existe pas de second poste »).

**Non ré-exécuté** par cette session (aucun accès shell aux invités) : le test de ping lui-même. Il est
**attesté par l'opérateur** via la description du snapshot horodaté, prise **après** succès du test
(le snapshot documente explicitement « Communication bidirectionnelle validée »).

### 15.5 Snapshots et capacité de rollback

Confirmé indépendamment via `VBoxManage snapshot <vm> list --machinereadable` (lecture seule) :

| Machine | Snapshot(s) | Snapshot courant |
|---|---|---|
| `MMV-SRV` | `00-CLEAN-WINDOWS` (Windows propre, avant réseau) → `01-LAB-READY` (réseau + pare-feu + connectivité validés, aucun provider) | **`01-LAB-READY`** |
| `MMV-CLI` | `01-LAB-READY` | **`01-LAB-READY`** |

Description du snapshot `01-LAB-READY` (identique sur les deux VM, lue directement via `VBoxManage`) :

> « Windows 11 Enterprise Evaluation propre. Guest Additions installées. MMV-SRV : 10.20.30.10. MMV-CLI :
> 10.20.30.20. NAT + réseau interne MMV-LAB. Profil privé et règles ICMP limitées. Communication
> bidirectionnelle validée. Aucun provider de base de données installé. »

Ceci **lève le blocage §5/§9/§12** (« aucune capacité de snapshot d'invité Windows disponible, Home sans
Hyper-V ») : VirtualBox fournit nativement le snapshot/rollback d'invité, indépendamment de l'édition Windows
de l'**hôte**. La **capacité de rollback est confirmée** : chaque VM peut être restaurée à `01-LAB-READY`
(état réseau prêt, aucun provider) ou, pour `MMV-SRV`, plus loin à `00-CLEAN-WINDOWS` (avant toute
configuration réseau), par une simple restauration de snapshot VirtualBox — aucune réinstallation requise.

### 15.6 Provider de base de données et données présentes

- **Aucun provider de base de données installé à cette date (2026-07-25, état `01-LAB-READY`)** sur `MMV-SRV`
  ni `MMV-CLI` — attesté explicitement par la
  description du snapshot `01-LAB-READY` (« Aucun provider de base de données installé »), cohérent avec
  l'absence totale de toute mention PostgreSQL/SQL Server dans la configuration VM elle-même (`showvminfo`
  n'expose aucun service, ce qui est attendu — un service invité n'est de toute façon pas visible depuis
  l'hôte sans agent applicatif).
- **Aucune donnée de production ni personnelle** : VM neuves, provisionnées spécifiquement pour ce
  laboratoire, sans dépôt applicatif MMV ni jeu de données réel copié à ce stade (aucune étape d'installation
  ni de migration n'a été entreprise — seul le socle réseau existe).

### 15.7 Réévaluation des prérequis Lot C (§12)

| # | Prérequis (§12) | État en 2026-07-25 | Preuve |
|---|---|---|---|
| 1 | VM serveur Windows propre / lab dédié | ✅ **Satisfait** — `MMV-SRV`, VirtualBox, Windows 11 Enterprise Evaluation | §15.1, `showvminfo` |
| 2 | Seconde VM/machine Windows cliente | ✅ **Satisfait** — `MMV-CLI`, VM VirtualBox distincte | §15.1, `showvminfo` |
| 3 | Connectivité réseau privé entre les deux | ✅ **Satisfait** — réseau interne `MMV-LAB`, ping bidirectionnel attesté | §15.2, §15.4 |
| 4 | Droits administrateur sur la VM serveur | ⚠️ **Non re-vérifié par cette session** (pas d'accès shell à l'invité) ; utilisateur `AdminLab` connecté constaté via `GuestInfo/OS/LoggedInUsersList` | §15.1 |
| 5 | Capacité de snapshot/rollback | ✅ **Satisfait** — snapshots VirtualBox natifs, `01-LAB-READY` courant sur les deux VM | §15.5 |
| 6 | Aucune donnée de production/personnelle | ✅ **Satisfait** — VM neuves, snapshot atteste « aucun provider installé » | §15.5, §15.6 |
| 7 | Espace disque suffisant | ⚠️ **Non re-vérifié** dans cette revalidation (hors périmètre : le disque hôte D:/Y: avait déjà été constaté large en §10 ; disques invités des VM non interrogés) | — |
| 8 | Tester PostgreSQL et SQL Server séparément depuis un état Windows propre | ⏳ **Restait à exécuter au 2026-07-25** — lab prêt (`01-LAB-READY`), aucune installation de provider à cette date. **Exécuté depuis** : voir §17 (PostgreSQL `PASS`) et §19 (SQL Server `PASS`) | §15.6, §16 *(constat du 2026-07-25)* ; §17, §19, §20 *(état courant)* |

**Garde-fou « station de développement non réutilisée comme lab »** : toujours respecté — le laboratoire est
constitué de VM VirtualBox dédiées (`MMV-SRV`, `MMV-CLI`), distinctes de la station de développement
`DESKTOP-BPS3B6T` qui les héberge en tant qu'hyperviseur.

### 15.8 Ce que cette revalidation n'a pas fait

Conformément au mandat de revalidation (porte de disponibilité uniquement) :

- **Aucun** provider de base de données installé ou modifié (ni PostgreSQL, ni SQL Server).
- **Aucune** modification de pare-feu, de configuration TCP, de service, ou de configuration VM — toutes les
  commandes `VBoxManage` exécutées sont **strictement en lecture** (`showvminfo`, `snapshot list`,
  `guestproperty enumerate`).
- **Aucun** démarrage/arrêt de VM par cette session (`MMV-SRV` était déjà `running`, `MMV-CLI` déjà
  `poweroff` au moment du constat).
- **Aucun** choix ni élimination de provider ; **aucune** ADR P4-2.
- **Aucun** commit, **aucun** push (modifications documentaires locales uniquement, sur arbre de travail).

---

## 16. Verdict revalidé (2026-07-25) *(historique — remplacé)*

> **Verdict §16 remplacé.** `P4-1 LOT C = NOT STARTED` reflète l'état du **2026-07-25**, *avant* toute
> installation de provider dans le laboratoire. Le Lot C a **depuis été exécuté** : voir §17 (piste PostgreSQL)
> et §19 (piste SQL Server), et **§20** pour le verdict en vigueur (`P4-1 LOT C = PASS`). La ligne
> `P4-1 LOT C READINESS = READY TO EXECUTE` reste, elle, **valide et en vigueur**. **Le contenu évidentiel** de
> §16 (constat du laboratoire, réévaluation des prérequis) **est préservé, sans suppression ni réécriture
> silencieuse** ; seule une **annotation de supersession explicite** a été **ajoutée** sur la ligne de verdict
> ci-dessous marquée `[HISTORIQUE]`, qui ne décrit plus l'état courant.

```
P4-1 LOT C READINESS = READY TO EXECUTE
P4-1 LOT C            = NOT STARTED          [HISTORIQUE — remplacé par §20 : PASS]
P4-1 SPIKE            = INCOMPLETE
P4-2 ADR              = BLOCKED
```

**Lecture du verdict** : la **porte de disponibilité** du laboratoire (deux VM Windows isolées, réseau privé
fonctionnel, pare-feu actif avec ICMP restreint, snapshot/rollback confirmé, aucune donnée de production, aucun
provider) est **franchie** — ce qui était `BLOCKED` en §14 est désormais `READY TO EXECUTE`. Ceci **ne
constitue pas** l'exécution du Lot C lui-même : **aucune installation de PostgreSQL ou de SQL Server Express
n'a eu lieu**, donc **aucune preuve E1–E17 native Windows** n'existe encore, et **P4-1 SPIKE reste
`INCOMPLETE`** et **P4-2 ADR reste `BLOCKED`** jusqu'à l'exécution effective du Lot C (procédure §13) à partir
du snapshot `01-LAB-READY`.

**Base Git/CI reconfirmée pour cette revalidation** : branche `p4-multi-poste`, `HEAD` local et distant
`f9df67ac8753812fbb5dd22cc147a69ef19200d3` (identique à §1, aucun nouveau commit), CI de référence run
`30129393484` — `event=push` / `status=completed` / `conclusion=success` / `headSha` exact (revérifié
directement via `gh run view 30129393484` lors de cette revalidation).

**Contraintes respectées** : aucun provider choisi ou éliminé ; aucune ADR P4-2 ; aucune installation ;
aucune modification de pare-feu/TCP/service/VM ; aucun redémarrage de VM par cette session ; aucun commit ;
aucun push ; aucune donnée de production ou personnelle utilisée ou créée.

*Revalidation de la porte de disponibilité — Lot C, 2026-07-25. Laboratoire provisionné et vérifié
indépendamment via `VBoxManage` (lecture seule) + `gh run view`. Lot C lui-même reste à exécuter.*

---

## 17. Lot C — piste PostgreSQL exécutée manuellement (2026-07-25)

> **Portée de cette section.** Enregistrement des **preuves observées manuellement** par l'opérateur pour la
> **piste PostgreSQL** du Lot C, à partir du laboratoire dont la porte de disponibilité a été validée en
> §15–§16. Les tests eux-mêmes (installation, connexion réseau, CRUD, redémarrage, sauvegarde/restauration,
> coupure de service) ont été exécutés **à l'intérieur des VM invitées**, hors de portée d'accès shell de cette
> session ; ils sont **rapportés par l'opérateur** et **corroborés indépendamment**, dans la mesure du possible,
> par lecture seule de l'état VirtualBox (`VBoxManage snapshot list`). **Aucune** piste SQL Server n'est
> commencée ici. **Aucun** choix de provider.

### 17.1 Laboratoire (inchangé depuis §15)

`MMV-SRV` (Windows 11 Enterprise Evaluation, `10.20.30.10/24`) et `MMV-CLI` (Windows 11 Enterprise Evaluation,
`10.20.30.20/24`), reliées par le réseau interne VirtualBox **`MMV-LAB`** — configuration identique à §15.2,
aucun changement réseau structurel signalé pour cette piste au-delà de la règle de pare-feu PostgreSQL (§17.3).

### 17.2 Version PostgreSQL et service Windows

| Élément | Valeur rapportée |
|---|---|
| Version serveur PostgreSQL | **17.10** |
| Version client (`psql`/outils) | **17.10** |
| Service Windows | **`postgresql-x64-17`** |
| Type de démarrage du service | **Automatique**, confirmé |
| Interfaces d'écoute | `127.0.0.1`, `::1`, **`10.20.30.10`**, port **`5432`** |

### 17.3 Pare-feu Windows — règle PostgreSQL

- **Pare-feu Windows demeure activé** sur `MMV-SRV` (aucune désactivation globale).
- Une règle a été créée, restreignant l'accès PostgreSQL à : protocole **TCP**, port **5432**, interface
  **Ethernet 2** (l'adaptateur interne `MMV-LAB`), adresse locale **`10.20.30.10`**, adresse distante
  **`10.20.30.20`**.
- **Correctif de profil rapporté** : le profil de la règle a dû être changé à **`Any`** (au lieu d'un profil
  `Private`/`Domain` strict), parce que l'adaptateur réseau interne isolé de VirtualBox est **reclassé `Public`
  par Windows après redémarrage** (comportement Windows connu pour les réseaux sans passerelle détectée). Les
  restrictions **d'interface, de port et d'adresse** (Ethernet 2, TCP 5432, `10.20.30.10` ↔ `10.20.30.20`)
  **sont demeurées en vigueur** malgré ce changement de profil — **aucun assouplissement d'adresse ou de
  port** n'a été introduit, seul le champ *profil* de la règle a été élargi pour rester effective après un
  reclassement de réseau hors du contrôle de l'opérateur.
- **Aucune désactivation globale du pare-feu** n'a été effectuée à aucun moment.

### 17.4 Connectivité réseau réelle distante

| Mesure | Résultat rapporté |
|---|---|
| Accès TCP initial (avant configuration pare-feu) | **`false`** |
| Accès TCP après configuration | **`true`** |
| Connexion `MMV-CLI` → `MMV-SRV` | **Réussie** |
| Adresse serveur vue côté PostgreSQL | `10.20.30.10` |
| Adresse client vue côté PostgreSQL | `10.20.30.20` |
| Rôle connecté | `mmv_app` |
| Base connectée | `mmv_p4_lab` |

Le passage `false` → `true` démontre que c'est bien la **règle de pare-feu** (§17.3), et non une autre
condition réseau, qui conditionnait l'accès TCP distant.

### 17.5 CRUD distant

- Table **`network_test`** créée à distance depuis `MMV-CLI`.
- Une ligne insérée à distance.
- Lecture réussie de la ligne : `id = 1`, `message = "Connexion MMV-CLI vers MMV-SRV OK"`.

### 17.6 Preuve de redémarrage Windows

- `MMV-SRV` a été **redémarrée**.
- Le service `postgresql-x64-17` est **revenu automatiquement à l'état `Running`** (cohérent avec le type de
  démarrage « Automatique », §17.2) — **aucun démarrage manuel du service** n'a été requis après redémarrage.
- PostgreSQL a continué d'écouter sur `10.20.30.10:5432` après redémarrage.
- L'accès TCP depuis `MMV-CLI` a réussi **après correction du profil de pare-feu** (§17.3) — c'est-à-dire que
  le reclassement réseau post-redémarrage (`Public`) a nécessité le correctif de profil pour que l'accès
  redevienne possible ; une fois corrigé, l'accès a réussi.

### 17.7 Sauvegarde et restauration

- Sauvegarde créée au **format personnalisé** (`custom format`) avec **`pg_dump` 17.10**.
- Le contenu de l'archive (listing) contient `public.network_test`, ses données, et sa **contrainte de clé
  primaire**.
- Une base de restauration **`mmv_p4_restore`** a été créée.
- **`pg_restore`** s'est terminé avec succès.
- La ligne restaurée dans `network_test` **correspond** à la donnée source (§17.5).

### 17.8 Coupure et reprise du service

- Le service PostgreSQL a été **arrêté manuellement**.
- `MMV-CLI` a observé **`TcpTestSucceeded = False`**.
- Le service PostgreSQL a été **redémarré**.
- `MMV-CLI` a observé **`TcpTestSucceeded = True`**.
- L'authentification **`psql`** distante a de nouveau réussi.
- La ligne d'origine dans `network_test` **est restée présente** après la reprise (aucune perte de donnée liée
  à la coupure).

### 17.9 Snapshots

Confirmé **indépendamment** via `VBoxManage snapshot <vm> list --machinereadable` (lecture seule, exécuté par
cette session) :

| Machine | Nouveau snapshot | Snapshot parent |
|---|---|---|
| `MMV-SRV` | **`02-POSTGRESQL-PASS`** | `01-LAB-READY` (lui-même issu de `00-CLEAN-WINDOWS`) |
| `MMV-CLI` | **`02-POSTGRESQL-PASS`** | `01-LAB-READY` |

Description du snapshot `02-POSTGRESQL-PASS` (identique sur les deux VM, lue directement) :

> « PostgreSQL 17.10 validé sur Windows. Connexion MMV-CLI 10.20.30.20 vers MMV-SRV 10.20.30.10. TCP 5432,
> authentification SCRAM et CRUD validés. Redémarrage Windows validé. Arrêt et reprise du service validés.
> Sauvegarde et restauration validées. Données persistantes après coupure. »

Cette description **corrobore indépendamment** l'essentiel des preuves rapportées en §17.2–§17.8 (version,
adresses, TCP 5432, CRUD, redémarrage, arrêt/reprise, sauvegarde/restauration, persistance), et confirme que
`01-LAB-READY` reste disponible comme **point de rollback antérieur à tout provider** (§15.5), inchangé.

### 17.10 Portée et limites explicites de la preuve

- Ces preuves valident PostgreSQL 17.10 **au niveau réseau/Windows/service/SQL brut** (via `psql`/outils
  serveur), **pas** l'intégration applicative MMV : **aucun code de `MMV.App`, `MMV.Infrastructure`, ou de
  l'`OpticDbContext` n'a été exercé** ; le rôle `mmv_app` et la base `mmv_p4_lab` sont des objets PostgreSQL de
  test, **pas** une exécution de l'application MMV elle-même ni de ses primitives EF Core (portage des **14**
  primitives de l'inventaire **réconcilié** — l'audit §15 en recensait 15, comptage **historique** avant retrait
  du doublon conceptuel au Lot A — **non couvert par cette section**).
- **Aucun choix de provider** n'est fait : ceci est une preuve technique PostgreSQL, à mettre en regard d'une
  piste SQL Server équivalente, **non commencée à la date de cette section** *(historique : cette piste a
  depuis été exécutée et est `PASS` — voir §19)*.
- **Aucune modification de code applicatif** n'a été effectuée par cette session (`src/MMV.App/**` et le reste
  de `src/**` non touchés).
- Les commandes exécutées par **cette session** (chat) sont strictement en lecture (`VBoxManage snapshot
  list`, `gh run view`, `git status`/`git rev-parse`) ; les tests PostgreSQL eux-mêmes (§17.2–§17.8) ont été
  **exécutés par l'opérateur** à l'intérieur des invités, hors d'atteinte de cette session.

---

## 18. Verdict mis à jour (2026-07-25 — piste PostgreSQL) *(historique — remplacé)*

> **Verdict §18 remplacé.** Il décrit l'état **intermédiaire** du 2026-07-25, alors que seule la piste
> PostgreSQL avait été exécutée. La piste SQL Server a **depuis** été exécutée (§19) et le Lot C **clos**
> (§20, `P4-1 LOT C = PASS`). **Le contenu évidentiel** de §18 **est préservé, sans suppression ni réécriture
> silencieuse** ; seules des **annotations de supersession explicites** ont été **ajoutées** sur les lignes
> marquées `[HISTORIQUE]` ci-dessous, qui ne décrivent plus l'état courant. Les **preuves PostgreSQL de §17
> restent valides et inchangées**.

```
P4-1 LOT C READINESS        = READY TO EXECUTE
P4-1 LOT C POSTGRESQL TRACK = PASS
P4-1 LOT C SQL SERVER TRACK = NOT STARTED   [HISTORIQUE — remplacé par §20 : PASS]
P4-1 LOT C                  = IN PROGRESS   [HISTORIQUE — remplacé par §20 : PASS]
P4-1 SPIKE                  = INCOMPLETE
P4-2 ADR                    = BLOCKED
```

**Lecture du verdict** : la piste **PostgreSQL** du Lot C est **passée** (`PASS`) sur les critères réseau réel
depuis un second poste Windows — connexion TCP distante, CRUD distant, redémarrage Windows, arrêt/reprise de
service, sauvegarde/restauration, persistance après coupure — tous **manuellement rapportés** et corroborés par
le snapshot `02-POSTGRESQL-PASS`. **Cela ne clôt pas le Lot C** : la piste **SQL Server Express** n'a **pas
commencé** (`NOT STARTED`), donc le Lot C dans son ensemble est **`IN PROGRESS`**, pas terminé. **P4-1 SPIKE
reste `INCOMPLETE`** et **P4-2 ADR reste `BLOCKED`** tant que la piste SQL Server n'a pas produit une preuve
équivalente et que la matrice comparative (§13, point D) n'est pas consolidée. **Aucun provider n'est choisi ou
éliminé** par ce résultat : un `PASS` PostgreSQL n'est pas une décision d'ADR.

**Base Git/CI reconfirmée à nouveau** : `HEAD` local `f9df67ac8753812fbb5dd22cc147a69ef19200d3` (inchangé,
aucun nouveau commit), CI de référence run `30129393484` — `conclusion=success`, `headSha` exact (revérifié via
`gh run view 30129393484`).

**Contraintes respectées** : aucune modification de code applicatif (`src/**`, y compris `src/MMV.App/**`,
intact) ; aucun choix ni élimination de provider ; aucune ADR P4-2 ; piste SQL Server non commencée ; aucun
commit ; aucun push.

*Preuve manuelle piste PostgreSQL — Lot C, 2026-07-25. Snapshot `02-POSTGRESQL-PASS` confirmé indépendamment
sur `MMV-SRV` et `MMV-CLI`. Piste SQL Server et clôture du Lot C restent à exécuter.*

> **Verdict §18 remplacé.** La piste SQL Server, `NOT STARTED` ci-dessus, a depuis été exécutée : voir
> **§19** (preuves) et **§20** (verdict en vigueur). **Le contenu évidentiel** de §18 **est préservé, sans
> suppression ni réécriture silencieuse**, comme trace de l'état intermédiaire ; seule une **annotation de
> supersession** y a été **ajoutée**. Les preuves PostgreSQL de §17 restent valides et inchangées.

---

## 19. Lot C — piste SQL Server Express exécutée manuellement (consigné le 2026-07-26)

> **Portée de cette section.** Enregistrement des **preuves observées manuellement** par l'opérateur pour la
> **piste SQL Server Express** du Lot C, dans le même laboratoire que §15–§18. Comme pour la piste PostgreSQL,
> les tests eux-mêmes (installation, configuration réseau, connexion distante, CRUD, redémarrage,
> sauvegarde/restauration, coupure de service) ont été exécutés **à l'intérieur des VM invitées**, hors de
> portée d'accès shell de cette session ; ils sont **rapportés par l'opérateur** et **corroborés
> indépendamment**, dans la mesure du possible, par lecture seule de l'état VirtualBox (`VBoxManage snapshot
> list`). **Aucun** choix ni élimination de provider n'est effectué ici. **Aucune** ADR P4-2.

### 19.1 Laboratoire

Identique à §15 et §17, inchangé structurellement :

| Élément | Valeur |
|---|---|
| Serveur | `MMV-SRV` — Windows 11 **Enterprise Evaluation**, `10.20.30.10/24` |
| Client | `MMV-CLI` — Windows 11 **Enterprise Evaluation**, `10.20.30.20/24` |
| Réseau | Réseau **interne** VirtualBox **`MMV-LAB`** (isolé du LAN de l'hôte) |
| Pare-feu Windows | **Demeuré activé** sur les deux machines pendant toute la piste |
| Données | **Aucune** donnée de production ni utilisateur employée |
| Rollback propre | Snapshot **`01-LAB-READY`** présent sur les deux VM (état réseau prêt, aucun provider) |
| Rollback PostgreSQL | Snapshot **`02-POSTGRESQL-PASS`** présent sur les deux VM |
| Rollback SQL Server | Snapshot **`03-SQLSERVER-PASS`** présent sur les deux VM |

Les trois snapshots sont **confirmés indépendamment** par cette session (§19.12).

### 19.2 Installation SQL Server et service Windows

| Élément | Valeur rapportée |
|---|---|
| Produit | **SQL Server 2022 Express**, installé sur `MMV-SRV` |
| Instance | **nommée `SQLEXPRESS`** |
| Composant | *Database Engine Services* — installation **réussie** |
| Service Windows | **`MSSQL$SQLEXPRESS`** |
| Type de démarrage | **Automatique** |
| Après redémarrage Windows | Service revenu **automatiquement** à l'état `Running` |

> **Note par rapport à §2.** L'échec d'installation native `0x84C4000E` mentionné en §2 avait été constaté sur
> la **station de développement** (`DESKTOP-BPS3B6T`, socle non propre). Ici, depuis un socle Windows **propre
> et jetable**, l'installation des *Database Engine Services* a **réussi**. Ceci ne détermine pas la cause
> racine de l'échec antérieur, qui reste **non élucidée** ; il est seulement constaté qu'il ne s'est pas
> reproduit sur un état Windows propre.

### 19.3 Protocoles réseau et port TCP

| Protocole / paramètre | État rapporté |
|---|---|
| Shared Memory | **Activé** |
| Named Pipes | **Désactivé** |
| TCP/IP | **Activé** |
| Port TCP | **Fixe `1433`** |
| Écoute effective | SQL Server écoutait sur **TCP 1433** après configuration **et après redémarrage** |
| SQL Server Browser | **Resté désactivé** pour la connectivité d'exécution |
| UDP 1434 | **Non requis** (port fixe côté client, pas de résolution d'instance par le Browser) |

Le port fixe permet aux clients de se connecter par `tcp:10.20.30.10,1433` sans dépendre du service Browser
ni du port UDP 1434.

### 19.4 Pare-feu Windows — règle SQL Server

- **Accès TCP distant initial, avant la règle dédiée : non accepté.**
- Une **règle entrante dédiée** a été créée pour SQL Server.
- **Profil de la règle : `Any`**, pour la même raison qu'en §17.3 — le réseau interne VirtualBox isolé peut
  être **reclassé `Public` par Windows après redémarrage**.
- La règle est **restée restreinte** à :
  - protocole **TCP** ;
  - port local **1433** ;
  - interface **`Ethernet 2`** (l'adaptateur interne `MMV-LAB`) ;
  - adresse locale **`10.20.30.10`** ;
  - adresse distante **`10.20.30.20`**.
- **Aucune désactivation globale du pare-feu** n'a été effectuée.
- Aucune ouverture n'a été faite pour le SQL Server Browser ni pour UDP 1434 (non requis, §19.3).

Seul le champ **profil** a été élargi ; les restrictions **de protocole, de port, d'interface et d'adresses**
sont demeurées en vigueur — traitement identique à celui de la piste PostgreSQL, ce qui rend les deux pistes
comparables sur le plan de l'exposition réseau.

### 19.5 Connectivité réseau réelle distante

| Mesure | Résultat rapporté |
|---|---|
| Accès TCP distant **avant** la règle de pare-feu | **Non accepté** |
| `Test-NetConnection` distant depuis `MMV-CLI` **après** la règle | **`TcpTestSucceeded = True`** |
| Chaîne de connexion cliente | `tcp:10.20.30.10,1433` |
| Outil client sur `MMV-CLI` | **`sqlcmd` v1.10.0** |

### 19.6 Connexion administrative distante

| Élément | Valeur observée |
|---|---|
| Authentification | **SQL**, compte `sa` — **réussie à distance** |
| Instance rapportée par SQL Server | **`MMV-SRV\SQLEXPRESS`** |
| Adresse serveur observée | `10.20.30.10` |
| Port serveur observé | `1433` |
| Adresse client observée | `10.20.30.20` |
| Login observé | `sa` |

> **Hygiène de secret.** Le mot de passe `sa` initialement exposé pendant la mise en place du laboratoire a été
> **remplacé**. Aucun mot de passe, ancien ou nouveau, n'est consigné dans ce dépôt — et aucun ne doit l'être.

### 19.7 Compte applicatif et base de données

- Base **`mmv_p4_lab`** créée.
- Login SQL **`mmv_app`** créé.
- Utilisateur de base **`mmv_app`** mappé sur le login `mmv_app`.
- `mmv_app` a reçu **`db_owner`** — **uniquement** pour ce laboratoire technique jetable.
- Authentification distante en tant que **`mmv_app` : réussie**.
- Sélection distante de la base **`mmv_p4_lab` : réussie**.

> ⚠️ **`db_owner` n'est pas un modèle de permissions de production** et ne doit pas être lu comme tel. C'est un
> raccourci de laboratoire jetable ; le modèle de permissions applicatif réel reste **à définir** et sort du
> périmètre du Lot C.

### 19.8 CRUD distant

Toutes les opérations ci-dessous ont été exécutées **à distance depuis `MMV-CLI`** :

| Opération | Résultat |
|---|---|
| Création de la table **`network_test`** | ✅ réussie à distance |
| **INSERT** d'une ligne | ✅ réussi à distance |
| **SELECT** de la ligne | ✅ lue à distance |
| **UPDATE** de la ligne | ✅ réussi à distance |
| **SELECT** de la valeur mise à jour | ✅ lue avec succès |
| **DELETE** de la ligne | ✅ réussi à distance |
| `SELECT COUNT(*)` après suppression | **`0`** |

**Piste CRUD distante : PASS.**

### 19.9 Redémarrage Windows et persistance

- Ligne de persistance insérée : `id = 1`, `message = "Persistance apres redemarrage OK"`.
- `MMV-SRV` a été **redémarrée**.
- **`MSSQL$SQLEXPRESS` est revenu automatiquement à l'état `Running`** (cohérent avec le démarrage
  **Automatique**, §19.2) — aucun démarrage manuel requis.
- Le port **TCP 1433 est demeuré en état `Listen`** après redémarrage.
- `Test-NetConnection` distant : **`True`** après redémarrage.
- Authentification `sqlcmd` distante en **`mmv_app` : réussie** après redémarrage.
- La **ligne de persistance est restée présente** après redémarrage.

### 19.10 Sauvegarde et restauration natives

| Étape | Résultat rapporté |
|---|---|
| Sauvegarde native de `mmv_p4_lab` | ✅ créée avec succès |
| Fichier de sauvegarde | `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\mmv_p4_lab.bak` |
| `BACKUP DATABASE ... WITH CHECKSUM` | ✅ terminé avec succès |
| `RESTORE VERIFYONLY` | ✅ terminé avec succès |
| `RESTORE FILELISTONLY` | identifie **`mmv_p4_lab`** et **`mmv_p4_lab_log`** |
| Restauration vers `mmv_p4_restore` (`WITH MOVE`) | ✅ réussie |
| Contenu de `network_test` restauré | contient la **ligne de persistance attendue** |

**Sauvegarde, vérification, restauration et intégrité des données restaurées : PASS.**

### 19.11 Coupure et reprise du service

- **`MSSQL$SQLEXPRESS` arrêté manuellement.**
- `MMV-CLI` a observé **`TcpTestSucceeded = False`**.
- **`MSSQL$SQLEXPRESS` redémarré** → service revenu à l'état **`Running`**.
- `MMV-CLI` a observé **`TcpTestSucceeded = True`**.
- Authentification `sqlcmd` distante : **de nouveau réussie**.
- La **ligne de persistance d'origine est restée présente** après reprise du service.

### 19.12 Snapshot

Confirmé **indépendamment** par cette session via `VBoxManage snapshot <vm> list --machinereadable` (lecture
seule) :

| Machine | Snapshot SQL Server | UUID | Parent | Snapshot courant |
|---|---|---|---|---|
| `MMV-SRV` | **`03-SQLSERVER-PASS`** | `1b2f1122-d83c-4d6f-81bb-56161f320ee7` | `01-LAB-READY` (frère de `02-POSTGRESQL-PASS`) | ✅ `03-SQLSERVER-PASS` |
| `MMV-CLI` | **`03-SQLSERVER-PASS`** | `a6f9f3bf-1711-410c-9405-e1b020e8fb92` | `02-POSTGRESQL-PASS` | ✅ `03-SQLSERVER-PASS` |

Description du snapshot `03-SQLSERVER-PASS` (identique sur les deux VM, lue directement) :

> « SQL Server 2022 Express validé sur Windows. Instance SQLEXPRESS. TCP fixe 1433. Connexion MMV-CLI vers
> MMV-SRV validée. Authentification SQL et CRUD distant validés. Redémarrage Windows et démarrage automatique
> validés. Arrêt et reprise du service validés. Sauvegarde, vérification et restauration validées. Données
> persistantes après redémarrage et récupération. »

Cette description **corrobore indépendamment** l'essentiel des preuves rapportées en §19.2–§19.11 (produit,
instance, port fixe 1433, connexion distante, authentification SQL, CRUD distant, redémarrage + démarrage
automatique, arrêt/reprise, sauvegarde/vérification/restauration, persistance).

**Détail de topologie constaté — énoncé précis, sans extrapolation** :

- Sur **`MMV-SRV`** — la **seule** machine hébergeant un **serveur** de base de données — `03-SQLSERVER-PASS`
  est un **frère** de `02-POSTGRESQL-PASS` **sous `01-LAB-READY`**. La piste SQL Server est donc bien partie
  d'un **retour à `01-LAB-READY`**, c'est-à-dire d'un **état serveur propre, sans PostgreSQL installé**,
  conformément à la procédure §13 point C.11.
- Sur **`MMV-CLI`**, `03-SQLSERVER-PASS` est au contraire **enchaîné après** `02-POSTGRESQL-PASS`. **La
  topologie de snapshots n'est donc pas identique entre les deux VM**, et ce document **ne revendique pas**
  qu'elle le soit.
- Cette différence **n'affecte pas la comparaison des installations natives de serveur de base de données**,
  car `MMV-CLI` a tenu un rôle **purement client** et n'a **hébergé aucun serveur de base de données** — seuls
  des outils clients y ont été employés (`psql` en §17, `sqlcmd` en §19). Elle n'est **pas** qualifiée
  d'« indifférente » au-delà de ce périmètre : d'éventuels résidus d'outils clients PostgreSQL sur `MMV-CLI`
  pendant la piste SQL Server ne sont **pas** couverts par un rollback et ne sont **pas** revendiqués comme
  absents.

**Énoncé retenu** : **les deux pistes de serveur de base de données ont été exécutées sur `MMV-SRV` à partir du
même état serveur propre `01-LAB-READY`.**

Les points de rollback `00-CLEAN-WINDOWS`, `01-LAB-READY` et `02-POSTGRESQL-PASS` demeurent tous disponibles et
intacts.

### 19.13 État comparatif des deux pistes

| Critère Lot C | PostgreSQL 17.10 (§17) | SQL Server 2022 Express (§19) |
|---|---|---|
| Installation Windows native depuis un socle propre | ✅ PASS | ✅ PASS |
| Service Windows en démarrage **Automatique** | ✅ `postgresql-x64-17` | ✅ `MSSQL$SQLEXPRESS` |
| Écoute TCP sur l'adresse du réseau privé | ✅ `10.20.30.10:5432` | ✅ `10.20.30.10:1433` |
| Pare-feu Windows **maintenu activé**, règle restreinte | ✅ | ✅ |
| Accès distant refusé **avant** règle, accepté **après** | ✅ | ✅ |
| Connexion réseau réelle depuis un **second poste Windows** | ✅ `MMV-CLI` | ✅ `MMV-CLI` |
| Authentification distante d'un compte applicatif jetable | ✅ `mmv_app` | ✅ `mmv_app` |
| CRUD distant | ✅ (create/insert/read) | ✅ (create/insert/read/update/delete + count = 0) |
| Redémarrage Windows → service auto + écoute + accès distant | ✅ | ✅ |
| Persistance des données après redémarrage | ✅ | ✅ |
| Sauvegarde + restauration avec outils natifs | ✅ `pg_dump` / `pg_restore` | ✅ `BACKUP`/`RESTORE` (+ `CHECKSUM`, `VERIFYONLY`) |
| Coupure de service → indisponibilité → reprise → données intactes | ✅ | ✅ |
| Snapshot de validation | ✅ `02-POSTGRESQL-PASS` | ✅ `03-SQLSERVER-PASS` |

**Les deux pistes de serveur de base de données ont été exécutées sur `MMV-SRV` à partir du même état serveur
propre `01-LAB-READY`** (§19.12). Aucune identité de topologie de snapshots n'est revendiquée pour `MMV-CLI`,
dont le rôle est **purement client** et qui n'a hébergé **aucun serveur de base de données**.

- Piste **PostgreSQL Windows/réseau** : **PASS**.
- Piste **SQL Server Windows/réseau** : **PASS**.

### 19.14 Portée et limites explicites de la preuve

- Ces tests prouvent, pour les **deux** providers : **installation Windows native**, **accès TCP distant réel**,
  **authentification**, **CRUD distant**, **persistance après redémarrage**, **reprise après coupure de
  service**, **sauvegarde et restauration**.
- Ces tests **ne prouvent pas** la **compatibilité applicative complète de MMV**. **Aucune** intégration au
  niveau applicatif n'a été testée : aucun code de `MMV.App`, `MMV.Application`, `MMV.Infrastructure` ni
  `OpticDbContext` n'a été exercé contre ces serveurs ; `mmv_app`, `mmv_p4_lab` et `network_test` sont des
  objets SQL de test créés à la main, **pas** une exécution de l'application MMV ni de ses primitives EF Core.
- **`NotificationRepository.TryCreateActiveLowStockAsync` demeure un bloqueur non résolu au niveau
  applicatif**, indépendant du résultat de ce laboratoire : l'implémentation actuelle
  ([NotificationRepository.cs:125-131](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L125-L131))
  construit des **`SqliteParameter`** avant l'exécution spécifique au provider, ce qui la rend non portable en
  l'état vers PostgreSQL comme vers SQL Server. Sur l'inventaire **réconcilié à 14 primitives distinctes**
  (l'audit P4-0 §15 en recensait **15** — comptage **historique**, doublon conceptuel retiré au Lot A),
  **13 / 14** ont été exercées via leurs **implémentations de production** aux Lots A et B ;
  `TryCreateActiveLowStockAsync` est la **14ᵉ**, **seule primitive restée en échec**, de façon **identique** sur
  les deux providers. Sa résolution constitue le **Lot D** (§21).
- **Par conséquent, aucun provider ne peut encore être choisi ni éliminé.**
- **Aucune** modification de code applicatif n'a été effectuée par cette session (`src/**`, y compris
  `src/MMV.App/**`, intact). Les commandes exécutées par **cette session** sont strictement en lecture
  (`VBoxManage snapshot list`, `gh run view`, `git status` / `git rev-parse` / `git log` / `git diff`) ; les
  tests SQL Server eux-mêmes (§19.2–§19.11) ont été **exécutés par l'opérateur** à l'intérieur des invités,
  hors d'atteinte de cette session.

---

## 20. Verdict mis à jour (2026-07-26 — clôture du Lot C) — **VERDICT EN VIGUEUR**

> Ce verdict **remplace** ceux de §14, §16 et §18. C'est le **seul** verdict courant du Lot C.

```
P4-1 LOT A                  = GO
P4-1 LOT B                  = GO
P4-1 LOT C READINESS        = READY TO EXECUTE
P4-1 LOT C POSTGRESQL TRACK = PASS
P4-1 LOT C SQL SERVER TRACK = PASS
P4-1 LOT C                  = PASS
P4-1 SPIKE                  = INCOMPLETE
P4-2 ADR                    = BLOCKED
```

**Lecture du verdict** : les **deux** pistes du Lot C sont **passées** (`PASS`) sur les critères
Windows/réseau — installation native depuis un socle propre, accès TCP distant réel depuis un second poste
Windows, authentification, CRUD distant, persistance après redémarrage, reprise après coupure de service,
sauvegarde et restauration — chacune corroborée par son snapshot de validation (`02-POSTGRESQL-PASS`,
`03-SQLSERVER-PASS`). Le **Lot C est donc `PASS`** : le critère manquant du spike P4-1 (installation Windows
native + réseau réel multi-poste) est **couvert**.

**Limites du Lot C, explicitement maintenues** :

- Le Lot C **prouve** : installation Windows, connectivité TCP, authentification brute, CRUD, redémarrage,
  reprise de service, sauvegarde/restauration — pour **les deux** providers.
- Le Lot C **ne prouve pas** l'**intégration applicative MMV**.
- **Aucun provider n'est choisi ni éliminé.**
- **P4-2 reste bloquée.**

**Le spike P4-1 reste donc `INCOMPLETE` et l'ADR P4-2 reste `BLOCKED`** : le Lot C valide le **socle
serveur**, pas l'**intégration applicative MMV**, qui n'a **pas** été testée. Le bloqueur applicatif
`NotificationRepository.TryCreateActiveLowStockAsync` (usage de `SqliteParameter` avant exécution spécifique au
provider, §19.14) demeure **non résolu** et affecte les deux providers. Sa résolution est le **prochain lot
technique bloquant**, défini en **§21 — P4-1 Lot D**.

**Aucun provider n'est choisi ni éliminé** par ce résultat : deux `PASS` symétriques ne constituent pas une
décision d'architecture. Aucune ADR P4-2 n'est créée.

**Base Git/CI reconfirmée** : branche `p4-multi-poste`, `HEAD` local
`f9df67ac8753812fbb5dd22cc147a69ef19200d3` (inchangé, aucun nouveau commit), CI de référence run
`30129393484` — `event=push` / `status=completed` / `conclusion=success` / `headSha` exact (revérifié via
`gh run view 30129393484` lors de cette mise à jour).

**Contraintes respectées** : aucune modification de code applicatif (`src/**`, y compris `src/MMV.App/**`,
intact) ; aucun choix ni élimination de provider ; aucune ADR P4-2 ; aucune revendication d'intégration
applicative MMV testée ; aucun mot de passe consigné ; aucune donnée de production ou utilisateur employée ;
aucun commit ; aucun push.

*Preuve manuelle piste SQL Server + clôture du Lot C — consigné le 2026-07-26. Snapshot `03-SQLSERVER-PASS`
confirmé indépendamment sur `MMV-SRV` et `MMV-CLI`. Prochain jalon bloquant : résolution du portage de
`TryCreateActiveLowStockAsync` et validation de l'intégration applicative MMV, préalables à l'ADR P4-2 —
**voir §21**.*

---

## 21. Prochain lot bloquant — **P4-1 Lot D : complétion de la portabilité applicative**

> **Portée de cette section.** Elle **définit** le prochain lot technique bloquant de P4-1, tel qu'il découle
> directement des limites explicites du Lot C (§19.14, §20). Elle **ne l'exécute pas** : aucun code n'est
> modifié ici, **aucun provider n'est choisi ni éliminé**, **aucune ADR P4-2** n'est créée.

### 21.1 Pourquoi le Lot D est bloquant

Le Lot C a couvert le **socle serveur** (installation Windows native, réseau, service, sauvegarde/restauration)
pour les **deux** candidats. Il **n'a pas** couvert l'**intégration applicative MMV**. Le seul critère du spike
P4-1 encore non satisfait est donc la **portabilité applicative**, et son unique point d'échec **mesuré** est
`NotificationRepository.TryCreateActiveLowStockAsync`.

### 21.2 Inventaire des primitives — comptage réconcilié en vigueur

| Élément | Valeur en vigueur |
|---|---|
| Primitives **distinctes** (inventaire réconcilié) | **14** |
| Primitives exercées via leurs **implémentations de production** (Lots A + B) | **13 / 14** |
| Primitive restée **en échec** | **1** — `NotificationRepository.TryCreateActiveLowStockAsync` |
| Nature de l'échec | construction d'objets **`SqliteParameter`** avant l'exécution spécifique au provider |
| Providers concernés | **les deux** — PostgreSQL **et** SQL Server, échec **identique** |

> **Comptage historique.** L'audit P4-0 §15 recensait **15** primitives ; un **doublon conceptuel** a été
> retiré lors du Lot A. Le **total réconcilié en vigueur est 14**. Les mentions de « 15 » dans les documents
> antérieurs sont **historiques** et ne doivent **pas** être réutilisées comme total courant.

### 21.3 Périmètre du Lot D

1. **Résoudre** `NotificationRepository.TryCreateActiveLowStockAsync`
   ([NotificationRepository.cs:90-131](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L90-L131)).
2. **Supprimer la construction d'objets `SqliteParameter`** avant l'exécution spécifique au provider.
3. **Préserver la garantie d'unicité de l'alerte LowStock active** — la décision doit rester prise **par la
   base, en une seule instruction**, et non par un `check-then-act` applicatif.
4. **Exercer l'implémentation de production** contre **PostgreSQL** *et* **SQL Server**.
5. **Prouver le comportement en concurrence** sur les **deux** providers (tentatives simultanées de création de
   la même alerte LowStock active).
6. **Ne sélectionner aucun provider pendant le Lot D.**

### 21.4 Interdits explicites du Lot D

- Choisir ou éliminer un provider.
- Créer l'ADR P4-2.
- Déplacer la garantie d'unicité vers l'application, ou l'affaiblir de quelque manière.
- Déclarer la primitive « portée » sans son test **sur chaque** provider.

### 21.5 Sortie attendue — cumulative, pas un nouveau départ

Le Lot D **ne rejoue pas** les preuves déjà acquises aux Lots A + B : les **13 primitives déjà exercées via
leurs implémentations de production** (§21.2) **restent acquises telles quelles** — le Lot D ne les remet pas
en cause et n'a pas à les re-prouver. Le Lot D **ajoute** uniquement la **14ᵉ** :
`TryCreateActiveLowStockAsync`, résolue puis exercée via son implémentation de production sur les **deux**
candidats, avec une **preuve de concurrence propre à cette primitive** (tentatives simultanées de création de
la même alerte LowStock active — §21.3 point 5).

**Résultat cumulé attendu : 14 / 14** primitives exercées via leurs **implémentations de production** sur les
**deux** candidats (13 acquises + 1 résolue par le Lot D). C'est la **condition d'entrée** de l'ADR P4-2, qui
demeure **`BLOCKED`** jusque-là.

```
P4-1 LOT C = PASS
P4-1 LOT D = REQUIRED — NEXT BLOCKING LOT
P4-1 SPIKE = INCOMPLETE
P4-2 ADR   = BLOCKED
```

*Définition du Lot D — consignée le 2026-07-26, sur la base `f9df67a` / CI `30129393484` (verte). Aucun code
modifié, aucun provider choisi ni éliminé, aucune ADR créée.*
