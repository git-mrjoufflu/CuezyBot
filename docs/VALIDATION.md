# Journal de validation

## 24 octobre 2026 — OBS WebSocket 5.x sous Linux

Environnement : Fedora 44, OBS 32.2.2, CuezyBot .NET 10 et Avalonia 12.1.4.

Résultats confirmés :

- le serveur OBS est accessible depuis CuezyBot sur un port personnalisé ;
- l’échange `Hello → Identify → Identified` et l’authentification par défi fonctionnent ;
- le mot de passe est saisi dans un champ masqué et reste uniquement en mémoire pour ce prototype ;
- la requête typée `SetCurrentProgramScene` est acceptée ;
- OBS sélectionne réellement la scène `Merci` ;
- la compilation se termine sans avertissement et les quatre tests automatisés passent.

À valider ensuite : reconnexion, réponse tardive, mot de passe incorrect, OBS indisponible pendant une exécution et stockage via le coffre de secrets du système.

## 24 octobre 2026 — OAuth Twitch et EventSub

Résultats confirmés :

- l’application Twitch publique `CuezyBot` fonctionne avec le Device Code Flow ;
- le compte utilisateur est validé sans secret client embarqué ;
- la permission `moderator:read:followers` est accordée et vérifiée ;
- la session EventSub WebSocket reçoit `session_welcome` ;
- Twitch accepte l’abonnement `channel.follow` version 2 pour le compte connecté ;
- CuezyBot reste en écoute des nouveaux followers.

À valider ensuite : réception d’un follower réel, dédoublonnage de sa notification, incrément durable et changement réel de scène OBS dans la même exécution.

## 24 octobre 2026 — Premier parcours réel Twitch → SQLite → OBS

Déclencheur : le compte de test `mrjoufflubot` suit la chaîne connectée.

Résultats confirmés :

- EventSub livre une notification `channel.follow` version 2 avec son `message_id` ;
- CuezyBot enregistre l’événement dans l’inbox SQLite ;
- une demande durable est créée puis passe de `Pending` à `Running` et `Succeeded` ;
- le compteur persistant `followersTraites` prend la valeur 1 ;
- l’étape compteur est journalisée `Succeeded` ;
- OBS accepte `SetCurrentProgramScene` et la scène `Merci` est sélectionnée ;
- l’étape OBS est journalisée `Succeeded` ;
- les données se trouvent dans `data/automation.db` à côté de l’exécutable Desktop.

À valider ensuite : rediffusion contrôlée du même `message_id`, redémarrage avec récupération des connexions, panne OBS pendant l’exécution et journal visible depuis l’interface.

## 24 octobre 2026 — Reconnexion automatique sécurisée

Résultats confirmés après fermeture complète et nouveau lancement de CuezyBot :

- le mot de passe OBS et les jetons Twitch sont présents dans le coffre Secret Service/KWallet sans être affichés ;
- l’adresse et le port OBS non sensibles sont relus depuis SQLite ;
- CuezyBot rétablit automatiquement la connexion OBS sur le port configuré ;
- le jeton Twitch est validé et la session EventSub est recréée automatiquement ;
- aucune saisie de mot de passe et aucune nouvelle autorisation Twitch ne sont demandées ;
- l’interface affiche `Connecté automatiquement : OBS + Twitch` ;
- l’implémentation Windows utilise Credential Manager pour les mêmes contrats de secrets.

Le renouvellement des jetons Twitch publics est implémenté avec rotation du refresh token et mise à jour du coffre. Son déclenchement réel à l’expiration reste à observer sur une session longue.

## 25 octobre 2026 — Routage des événements Twitch supplémentaires

Le catalogue de dix événements est relié au menu « + Événement », aux abonnements EventSub, aux workflows publiés et à l’historique SQLite : follow, abonnement, réabonnement, cadeaux, cheer, raid entrant, récompense personnalisée, live en ligne/hors ligne et informations de chaîne.

Validation automatisée : serveur WebSocket local avec réponses HTTP simulées pour les abonnements, permissions complètes ou partielles ; parsing des dix types avec conservation du payload ; routage des branches raid/cheer ; restauration des cartes multiples, langue FR/EN et sérialisation des liens explicites. Les tests de workflow vérifient aussi la restauration exacte des cartes, des positions et des liens après changement de canevas et annulation.

Les nouveaux accès OAuth sont `channel:read:subscriptions`, `bits:read` et `channel:read:redemptions`. Un jeton existant doit être réautorisé pour ces événements. La publication signale une permission manquante ou un abonnement refusé par Twitch. Aucun événement réel supplémentaire ni action sur un compte de production n’a été déclenché pendant ces tests. Les autres familles Twitch/OBS ne font pas partie de ce lot.
