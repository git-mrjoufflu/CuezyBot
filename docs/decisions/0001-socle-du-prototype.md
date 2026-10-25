# ADR 0001 — Socle du prototype

Date : 24 octobre 2026  
Statut : accepté

## Contexte

Le premier produit utile doit recevoir un follower Twitch, incrémenter un compteur durable et demander un changement de scène OBS. L’éditeur futur représente ce parcours avec des cartes visuelles, mais le moteur ne doit dépendre ni d’Avalonia ni de leur position à l’écran.

## Décision

- Utiliser .NET 10 pour les modules communs Windows/Linux.
- Séparer Domain, Contracts, Engine et Runtime dès le premier code.
- Donner à chaque type de bloc un identifiant texte et une version stables.
- Valider un workflow avant sa publication avec le même catalogue que celui exposé à l’éditeur.
- Commencer avec quatre blocs : follower Twitch, Si/Sinon, incrément de compteur et changement de scène OBS.
- Réserver SQLite et les connecteurs réels à la prochaine tranche verticale, une fois le contrat du workflow compilable.

## Conséquences

La représentation visuelle pourra évoluer sans modifier le plan exécutable. Les prochains modules de stockage, Twitch, OBS et interface dépendront des contrats communs plutôt que les uns des autres.

Fedora 44 reste une cible à valider directement : la documentation Avalonia consultée le 24 octobre 2026 répertorie Fedora 43. Sous Linux, X11/XWayland sera le chemin initial ; le backend Wayland natif demeure une validation ultérieure.
