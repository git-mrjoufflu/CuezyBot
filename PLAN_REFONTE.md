# Plan de refonte — Automatisation visuelle pour le streaming

Date : 24 octobre 2026  
Statut : prototype fonctionnel en développement continu
Plateformes cibles : Windows et Linux  
Public : utilisateurs ayant des connaissances faibles à moyennes en informatique

## État vérifié — 26 octobre 2026

Cette section prévaut sur les anciennes descriptions de maquette ci-dessous. Le projet n’est pas encore une version finale distribuable.

| Étape | État vérifié | Travail restant |
|---|---|---|
| 0 — Cadrage | Architecture, stockage portable, coffre système et verrou d’instance implémentés | Matrice définitive des environnements pris en charge |
| 1 — Expérience | Éditeur utilisable avec ajouts depuis le canevas | Essais utilisateurs et visualisation du parcours exécuté |
| 2 — Moteur | Révisions, admission transactionnelle, déduplication, variables et historique implémentés | Validation réelle complète sous Windows |
| 3 — Interface | Workflows multiples, événements multiples, liens explicites, paramètres intégrés, annuler/rétablir et FR/EN | Fermeture du moteur, API locale séparée, audit de localisation et accessibilité |
| 4 — Fiabilité | Résultats incertains, annulation, récupération sans rejeu, verrou d’instance, sauvegardes SQLite, pré-migration et restauration hors ligne | Assistant graphique de restauration, coupures réseau, disque plein, rétention et limites de capacité |
| 5 — Usages courants | Dix événements Twitch, actions OBS/Twitch, délais, conditions, variables et sons | Autres familles d’événements, commandes chat, modèles et audio OBS |
| 6 — Avancé | Cycles refusés et branches conditionnelles | Sous-automatisations, boucles bornées, parallélisme et scripts |
| 7 — Distribution | Compilation Windows et tests automatisés Windows/Linux configurés | Campagne installation/mise à jour/restauration ; installateur non prioritaire |

Dernières vérifications locales : 48 tests automatisés et 42 vérifications d’interface réussis. Les tests utilisent des données temporaires et des connecteurs simulés. La publication Windows a été vérifiée au lot précédent ; elle ne constitue pas un essai manuel complet sous Windows.

Les dix événements Twitch reliés à l’exécution sont : follow, abonnement, réabonnement, cadeaux, cheer, raid entrant, récompense personnalisée, début/fin de live et informations de chaîne. Seul le parcours follower → OBS possède une validation réelle documentée à ce jour.

Décisions UX actualisées : ajout par « + Événement », « + Action » (Twitch/OBS) et « + Système » (conditions, attente, variables et son) dans le canevas ; colonne droite retirée ; paramètres directement sur les cartes ; liens créés explicitement ; vert pour événements, mauve Twitch, gris OBS et bleu Système. « Tester » vérifie la structure ; « Exécuter » produit les effets de la branche manuelle publiée et alimente l’historique.

Navigation principale : Accueil, Workflows, Historique et Variables. Les anciens raccourcis Actions et Pages sont retirés tant qu’ils n’offrent pas une destination fonctionnelle; les actions sont ajoutées depuis le canevas et l’ancien menu Événements est renommé Historique conformément à sa fonction réelle.

L’état d’une automatisation est présenté par une switch Activer/Désactiver (Active/Inactive), plutôt que par un bouton Démarrer/Arrêter. L’activation publie le brouillon et arme ses déclencheurs; la désactivation empêche les nouveaux événements de lancer le parcours.

Une automatisation désactivée bloque également l’exécution réelle manuelle. Le bouton Tester reste accessible, puisqu’il valide le parcours sans produire d’effet Twitch ou OBS.

Enregistrer une automatisation active republie immédiatement une nouvelle révision afin que la prochaine exécution utilise les options visibles dans le canevas. Pour une automatisation inactive, Enregistrer conserve seulement le brouillon.

Une liaison qui crée une boucle n’est plus refusée automatiquement : l’interface demande une confirmation Oui/Non. Une boucle autorisée peut s’exécuter et le moteur impose une limite de sécurité de 1 000 étapes par exécution lorsqu’aucune sortie n’est atteinte.

Les variables persistantes de type Nombre ou Texte sont gérées depuis l’onglet Variables. La carte Système « Modifier une variable » utilise un champ unique : une valeur simple fixe le nombre, +5/-2 l’ajuste, x2 le multiplie et /2 le divise; une variable Texte reçoit simplement le texte saisi. Les champs texte exécutables acceptent la syntaxe %nom_variable%, résolue avec la valeur courante juste avant l’effet externe.

Une nouvelle automatisation commence sans événement automatique. Le bouton « + Événement » précède « + Action ». Les actions OBS comprennent aussi Enregistrement (démarrer/arrêter/pause/reprendre), Tampon replay (démarrer/arrêter/sauvegarder) et Caméra virtuelle (démarrer/arrêter), avec opérations intégrées aux cartes et captions FR/EN. Les neuf requêtes correspondantes sont vérifiées sur serveur WebSocket simulé ; les essais réels de ces sorties restent ouverts.

## 1. Vision et décisions retenues

Créer une application indépendante pour automatiser Twitch et OBS, avec un éditeur visuel inspiré de Power Automate. L’utilisateur construit un parcours de cartes, configure ses actions et comprend le résultat sans programmer.

Décisions validées :

- Windows et Linux sont pris en charge dès le premier prototype fonctionnel.
- L’éditeur visuel est l’interface principale dès la première version graphique.
- Les cartes sont positionnées sur le canevas et reliées explicitement ; aucun lien n’est ajouté automatiquement.
- La bibliothèque classe les blocs par catégories : OBS, Twitch, Core, puis extensions progressives.
- Les automatisations, brouillons, préférences et données durables sont enregistrés dans SQLite, dans `data` à côté du logiciel.
- Les modifications sont sauvegardées en brouillon ; la publication applique une version validée.
- Le moteur fonctionne indépendamment de l’interface : fermer la fenêtre ne doit pas interrompre silencieusement les automatisations.
- Le développement suit des jalons vérifiables, sans calendrier estimé avant validation du périmètre et des ressources.

Référence technique : [STREAMERBOT_ARCHITECTURE.md](../../REA-Projects/StreamerBot-Analysis/STREAMERBOT_ARCHITECTURE.md).

Ce plan précise et actualise cette référence : éditeur visuel dès la première interface, Windows testé dès le prototype et stockage portable à côté du logiciel. Les mécanismes de fiabilité décrits dans l’architecture restent applicables. Aucune modification du document de référence n’est effectuée ici.

## 2. Guidelines produit et ergonomie

### 2.1 Compréhension

- Présenter les automatisations sous la forme « Quand… », « Si… », « Alors… ».
- Employer des verbes concrets : « Changer de scène », « Envoyer un message », « Attendre ».
- Donner un exemple ou une courte explication aux paramètres difficiles.
- Afficher le comportement configuré directement sur la carte.
- Séparer les informations obligatoires des options avancées repliables.
- Résumer sur la carte toute option avancée qui change le comportement.
- Préférer des listes de scènes, sources et connexions à la saisie d’identifiants techniques.
- Signaler les données manquantes avant la publication et pointer la carte concernée.

### 2.2 Aspect visuel et accessibilité

- Navigation latérale stable, icônes accompagnées de textes et espaces généreux.
- Thèmes sombre et clair utilisant les mêmes composants et comportements.
- Une couleur d’accent principale ; statuts différenciés par texte et icône en plus de la couleur.
- Texte lisible, contrastes suffisants, focus visible et vérification de la mise à l’échelle.
- Création et modification complètes au clavier, sans obligation de glisser-déposer.
- Zoom, recentrage et déplacement du parcours ; conserver le bloc sélectionné lors des modifications.
- Animations courtes et désactivables ; aucune information essentielle accessible uniquement au survol.
- Même vocabulaire et mêmes gestes sur Windows et Linux.

#### Convention des boutons d’action

- Les actions répétitives d’administration utilisent des icônes plutôt que des libellés longs : `+` pour ajouter, crayon vert pour modifier, corbeille rouge pour supprimer et disquette claire pour enregistrer.
- Les icônes doivent être des formes vectorielles centrées, avec une taille et une hauteur identiques aux champs de la ligne ; éviter les caractères Unicode dont l’alignement varie selon la police.
- Chaque bouton iconographique conserve une infobulle et un nom d’accessibilité localisés en français et en anglais.
- Undo/Redo suivent la même dimension et le même traitement visuel que la disquette.
- Cette convention s’applique de façon uniforme aux Variables, Récompenses, Commandes, Files d’alertes, Automatisations et à toute nouvelle fenêtre de gestion.

### 2.3 Confiance et contrôle

- Sauvegarder automatiquement les brouillons et afficher « Enregistrement… », « Enregistré » ou une erreur explicite.
- Proposer annuler/rétablir pour l’édition du brouillon.
- Distinguer les états brouillon, publié et actif.
- Le bouton « Tester » utilise une simulation sans effets réels par défaut.
- Un essai réel doit être une action explicite, avec les effets prévus visibles.
- Une erreur explique le problème et propose une action utile.
- Une modification du brouillon ne change jamais une exécution déjà commencée.
- À la fermeture de la fenêtre, proposer clairement de laisser le moteur actif ou de l’arrêter.

## 3. Organisation de l’application

| Écran | Contenu principal |
|---|---|
| Accueil | État Twitch/OBS, moteur actif, automatisations et problèmes à résoudre |
| Mes automatisations | Recherche, groupes, activation, duplication, dernier résultat |
| Éditeur | Bibliothèque, parcours de cartes, paramètres et panneau de test |
| Modèles | Exemples prêts à personnaliser, avec prérequis visibles |
| Connexions | Assistant Twitch/OBS, diagnostic et reconnexion |
| Historique | Exécutions, chemin suivi, étapes et erreurs compréhensibles |
| Réglages | Apparence, données, sauvegardes et options avancées |

Les variables et files d’attente sont disponibles depuis les réglages avancés et les blocs concernés. Les scripts apparaîtront lorsque leur prise en charge sera livrée.

### 3.1 Composition de l’éditeur

| Zone | Comportement |
|---|---|
| Bibliothèque à gauche | Catégories dépliables, recherche globale et favoris |
| Parcours au centre | Cartes connectées, insertion par « + », branches nommées et placement automatique |
| Inspecteur à droite | Paramètres du bloc sélectionné, aide, validation et connexion cible |
| Panneau inférieur | Simulation, progression, résultats et messages d’erreur |
| Barre supérieure | Nom, état de sauvegarde, tester, publier et activer/désactiver |

Le menu ouvert par « + » reprend la bibliothèque et ne propose que les blocs autorisés à cet emplacement. Les connexions expriment une structure contrôlée : séquence et branches. Aucun câblage arbitraire n’est nécessaire pour les usages courants.

### 3.2 Catalogue de blocs

| Catégorie | Sous-catégories et exemples | Livraison |
|---|---|---|
| OBS | Scènes → changer de scène ; éléments → afficher/masquer ; audio → couper/rétablir le micro | Changement de scène au premier prototype, autres actions ensuite |
| Twitch | Déclencheurs → nouveau follower ; chat → message reçu/envoyer ; récompenses | Follower au premier prototype, autres événements ensuite |
| Core — Logique | Si/Sinon, attendre, journaliser, répéter, aléatoire, sous-automatisation | Condition simple au départ ; enrichissement progressif |
| Variables et compteurs | Lire, définir, incrémenter et comparer | Compteur durable au premier prototype |
| Audio | Jouer un son, régler un volume | Extension après stabilisation Twitch/OBS |
| Système | Ouvrir une application, lire/écrire un fichier | Extension avec capacités par plateforme |
| Favoris | Raccourcis vers les blocs choisis par l’utilisateur | Première interface utilisable |

Chaque bloc possède un type et une version stables, des paramètres typés, une description, ses permissions requises et ses capacités par plateforme. La disponibilité visuelle doit correspondre à une fonction réellement implémentée.

## 4. Guidelines techniques

- .NET 10 pour le moteur et Avalonia pour le bureau ; figer et vérifier les versions avant implémentation.
- Domain et moteur sans dépendance à l’interface, aux composants Windows ou aux assemblies propriétaires.
- Connecteurs indépendants fondés sur les contrats publics Twitch et OBS WebSocket 5.x.
- Un catalogue de blocs partagé fournit les schémas de paramètres et validations du moteur et de l’éditeur.
- Modèle d’automatisation versionné, indépendant des coordonnées et dimensions des cartes.
- Révisions publiées immuables ; chaque exécution conserve la révision utilisée.
- API locale versionnée entre le bureau et le moteur ; socket Unix sous Linux, canal nommé sous Windows.
- Horloge injectable, faux connecteurs et événements synthétiques pour tester sans compte réel.
- Aucun chargement de code ou d’assembly propriétaire dans la nouvelle application.
- Pas de chargeur de plugins tiers avant stabilisation des contrats.
- Toute opération propre à un système passe par un adaptateur ; sa disponibilité est visible dans l’interface.

## 5. Base de données locale et installation portable

Structure cible sous Windows ; sous Linux, l’exécutable porte le nom adapté :

```text
StreamerBot/
├── StreamerBot.exe
└── data/
    ├── automation.db
    └── backups/
```

Le nom commercial et les noms définitifs des exécutables restent à choisir.

### 5.1 Règles de stockage

- Résoudre `data` depuis le répertoire de l’application, jamais depuis le répertoire de travail du terminal.
- Vérifier l’écriture au démarrage. Si elle est impossible, expliquer comment déplacer le logiciel vers un dossier accessible ; ne pas basculer silencieusement vers un autre emplacement.
- Ne pas nécessiter de droits administrateur pour l’usage courant.
- Stocker les workflows, blocs, liens, disposition, brouillons, révisions, connexions sans secrets, favoris, préférences, variables persistantes, inbox, demandes et historique.
- Distinguer les variables d’exécution, de session en mémoire et persistantes en base.
- Le Runtime possède les écritures ; le bureau passe par l’API locale. Empêcher deux moteurs d’utiliser simultanément le même dossier de données.
- Gérer les migrations de schéma et sauvegarder avant une migration ; une ancienne version doit refuser proprement une base incompatible.
- Réaliser les sauvegardes avec un mécanisme SQLite cohérent. Une copie du fichier ouvert seule peut être insuffisante, notamment en mode WAL.
- Définir une rétention pour l’historique et les doublons ; rendre les pertes de capacité ou échecs d’enregistrement visibles.

### 5.2 Secrets et portabilité

Les jetons Twitch et mots de passe OBS passent par le coffre de secrets du système, via un adaptateur. SQLite conserve uniquement leurs références.

Déplacer le dossier transporte les automatisations et données locales. Les connexions peuvent nécessiter une nouvelle authentification sur une autre machine ou un autre compte système. Une exportation entièrement portable des secrets serait une fonctionnalité distincte à concevoir.

## 6. Plan de réalisation par étapes

### Étape 0 — Cadrage et contrats

Objectif : figer un premier périmètre réalisable.

- [ ] Définir les environnements Windows et Linux de référence et leurs versions prises en charge.
- [x] Vérifier les versions .NET/Avalonia et les contrats Twitch/OBS officiels.
- [x] Définir le premier schéma des workflows, cartes, connexions, révisions et résultats.
- [x] Définir le catalogue initial et le vocabulaire français.
- [x] Définir le stockage portable, le coffre de secrets et la règle d’instance unique. Le verrou du dossier de données est implémenté et testé.
- [x] Consigner les premières décisions dans des documents courts avec leurs raisons.

Critère de fin : un workflow d’exemple peut être décrit sans ambiguïté, de son événement source à ses effets et erreurs.

### Étape 1 — Maquette interactive de l’expérience

Objectif : valider la compréhension avant de développer toute l’interface.

- [x] Maquetter accueil, connexions, liste, éditeur et historique. Le premier écran d’éditeur est fonctionnel.
- [x] Montrer la bibliothèque OBS/Twitch/Core, la recherche et l’insertion par « + ».
- [x] Construire un premier parcours visuel avec déclencheur et actions ; la branche Oui/Non reste à visualiser.
- [ ] Simuler sauvegarde, publication, activation et erreur de connexion. Le retour visuel du test est présent.
- [ ] Montrer le chemin exécuté et les branches ignorées pendant un test.
- [ ] Faire essayer les tâches à des utilisateurs du public visé et relever leurs blocages.

Critère de fin : les participants peuvent créer, tester et désactiver une automatisation ; les incompréhensions observées sont corrigées ou consignées avant validation.

### Étape 2 — Socle et prototype moteur Windows/Linux

Objectif : démontrer la chaîne utile indépendamment de l’interface.

- [x] Créer les premiers modules Domain, Contracts, Engine, Persistence et Runtime nécessaires.
- [x] Initialiser SQLite, sa première migration et `data` à côté du logiciel.
- [x] Implémenter l’événement synthétique, la première file série durable et le journal corrélé par étape.
- [x] Enregistrer durablement la notification et créer ses demandes avec unicité et marqueur de routage transactionnels.
- [x] Incrémenter un compteur durable puis demander un changement de scène OBS, en simulation par défaut ou via une connexion OBS 5.x réelle.
- [x] Valider sur OBS Linux l’authentification WebSocket et la commande réelle `SetCurrentProgramScene` vers la scène `Merci`.
- [x] Ajouter OAuth Twitch par Device Code Flow public et EventSub `channel.follow` v2 avec identité et permission vérifiées.
- [x] Distinguer dans le domaine demande en attente, réussite, échec, annulation, interruption et résultat externe incertain ; les scénarios de panne complets restent à valider.
- [ ] Exécuter le prototype sur Windows et Linux.

Critère de fin : un événement synthétique puis un follower réel autorisé déclenchent la séquence. Le compteur survit au redémarrage ; le même message ne crée pas une deuxième demande dans la rétention prévue. OBS indisponible produit une erreur lisible.

État au 24 octobre 2026 : le follower réel, l’incrément SQLite, le journal durable et le changement de scène OBS sont validés sous Fedora. La reprise des connexions après redémarrage et les scénarios d’erreur réels restent ouverts avant de déclarer P0 terminé.

Mise à jour : la reprise automatique OBS et Twitch après redémarrage est désormais validée sous Fedora. Les scénarios de panne et le test Windows restent ouverts.

### Étape 3 — Première interface visuelle fonctionnelle

Objectif : configurer et utiliser le prototype sans modifier de JSON.

- [ ] Finaliser la séparation bureau/Runtime par API locale. L’interface Avalonia est implémentée ; le bureau appelle encore directement les services.
- [x] Livrer l’assistant de connexion Twitch/OBS et les diagnostics. OBS et Twitch disposent de fenêtres de connexion et de reconnexion persistante.
- [x] Livrer liste d’automatisations et éditeur avec placement automatique.
- [ ] Compléter recherche et favoris. Le catalogue et les paramètres intégrés existent ; le menu d’ajout a remplacé la bibliothèque latérale.
- [x] Sauvegarder brouillons et disposition ; annuler/rétablir couvre cartes, événements et liens.
- [x] Valider et publier une révision, puis activer/désactiver l’automatisation.
- [ ] Compléter la simulation et les résultats directement sur les cartes. La validation structurelle et l’historique par étape sont implémentés.
- [ ] Définir la fermeture de fenêtre et l’arrêt explicite du moteur.

Critère de fin : un utilisateur crée et teste Twitch → OBS depuis l’interface ; ses modifications réapparaissent après redémarrage. Fermer la fenêtre peut laisser le moteur actif selon le choix explicite présenté.

Note permanente : toute nouvelle fenêtre, alerte, bouton, statut, tooltip et message d’erreur doit être localisé en français et en anglais et suivre la langue choisie dès le démarrage. Les fenêtres de réglages, historique, intégrations, OBS et Twitch sont maintenant couvertes ; les messages d’erreur et le parcours de fermeture restent à harmoniser.

### Étape 4 — Fiabilité et reprise

Objectif : rendre les comportements après panne compréhensibles et prévisibles.

- [ ] Tester les ruptures Twitch ordinaires séparément des migrations de session demandées par Twitch.
- [ ] Gérer reconnexion OBS, échéances et réponses tardives.
- [ ] Vérifier les crashs pendant admission, routage et exécution.
- [ ] Garantir l’incrément atomique et la cohérence des marqueurs d’étapes locales.
- [ ] Ajouter cooldowns transactionnels, pause, annulation, expiration et limites de capacité.
- [x] Présenter les demandes interrompues sans rejouer aveuglément les effets externes.
- [ ] Tester sauvegarde, restauration, migration et dossier non accessible en écriture.

Critère de fin : les scénarios de panne ont des résultats reproductibles ; aucun effet externe incertain n’est présenté comme une réussite certaine.

### Étape 5 — Automatisations quotidiennes et modèles

Objectif : couvrir les usages courants avec peu de configuration.

- [ ] Ajouter visibilité des éléments OBS et commandes audio explicites.
- [ ] Ajouter chat Twitch, commandes et récompenses selon les besoins retenus.
- [ ] Ajouter délais annulables, conditions enrichies et édition de compteurs.
- [ ] Livrer des modèles avec explication et prérequis.
- [ ] Enrichir l’historique avec raisons de refus et données utiles masquées si sensibles.
- [ ] Définir la règle de restauration avant toute action temporaire qui remet un état OBS précédent.

Critère de fin : un ensemble choisi d’automatisations personnelles fonctionne dans la durée et possède des tests de résultat.

### Étape 6 — Logique avancée et scripts

Objectif : permettre la progression des utilisateurs intermédiaires.

- [ ] Ajouter sous-automatisations, répétitions bornées et parallélisme contrôlé.
- [ ] Rejeter cycles, attentes circulaires et conflits de types avant publication.
- [ ] Définir les règles de fusion des sorties des branches parallèles.
- [ ] Créer un SDK C# indépendant et un worker séparé avec diagnostics et échéances.
- [ ] Définir explicitement le niveau de confiance des scripts et les limites d’isolation.

Critère de fin : une erreur ou un blocage de script n’arrête pas le moteur ; les effets déjà demandés restent traçables.

### Étape 7 — Distribution et stabilisation

Objectif : livrer une application installable et maintenable sur les deux plateformes.

- [ ] Produire les paquets portables Windows/Linux et vérifier leurs dépendances.
- [ ] Tester une installation neuve, une mise à jour et une restauration.
- [ ] Préserver `data` lors des mises à jour ; livrer sauvegarde et export/import de notre format.
- [ ] Vérifier clavier, mise à l’échelle, thèmes, dialogues, tray et fermeture sur chaque environnement de référence.
- [ ] Fournir un guide de première utilisation et un diagnostic exportable sans secrets.
- [ ] Évaluer ensuite les extensions Audio/Système et les connexions multiples.

Critère de fin : installation et mise à jour réussissent sur les environnements ciblés sans perte de données, et les limites restantes sont documentées.

## 7. Scénarios de validation obligatoires

| Scénario | Résultat attendu |
|---|---|
| Première utilisation | Connexion guidée et première automatisation sans édition de fichier |
| Événement simulé | Parcours visible, aucun effet réel par défaut |
| Événement réel autorisé | Effet prévu et historique corrélé |
| Doublon après redémarrage | Aucune nouvelle demande dans la fenêtre de rétention |
| OBS indisponible | Erreur compréhensible sur la carte concernée |
| Arrêt après incrément, avant confirmation OBS | Incrément connu conservé, issue OBS expliquée, aucune relance globale automatique |
| Brouillon modifié pendant une exécution | L’exécution conserve sa révision de départ |
| Dossier non accessible en écriture ou disque plein | Échec visible, aucune fausse confirmation de sauvegarde |
| Sauvegarde et restauration | Automatisations, révisions et variables récupérables |
| Déplacement vers une autre machine | Données retrouvées ; réauthentification expliquée si nécessaire |
| Usage clavier et mise à l’échelle | Création, édition et test possibles sans obstacle majeur |
| Windows et Linux | Même workflow portable pour les blocs communs |

Les essais réseau réels constituent une campagne explicite sur comptes et installations autorisés. Les vérifications automatisées utilisent des adaptateurs factices par défaut.

## 8. Règle de fin pour chaque livraison

Une fonction est terminée lorsque son comportement est implémenté, expliqué dans l’interface, vérifié avec des cas utiles, persistant si nécessaire et validé sur les deux plateformes lorsqu’elle leur est commune. Les limitations sont visibles et consignées.

Prochaines actions : restauration guidée des sauvegardes, validation des coupures Twitch/OBS et fermeture explicite du moteur. Poursuivre ensuite modèles, événements supplémentaires et validation utilisateur Windows/Linux.

## Note permanente — Localisation complète

CuezyBot doit être disponible au minimum en français et en anglais. La langue choisie est enregistrée localement et doit être appliquée à toute l’application : fenêtre principale, Paramètres, connexion OBS, connexion Twitch, historique, boutons, statuts, erreurs, infobulles et messages système. Aucun écran ne doit rester partiellement dans une autre langue.
