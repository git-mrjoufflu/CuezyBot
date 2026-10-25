# Consignes de développement CuezyBot

- Toute interface, option, aide et erreur doit être disponible en français et en anglais.
- Regrouper les variantes d'une même action dans une seule entrée de bibliothèque (exemple : visibilité OBS avec Afficher/Masquer).
- Proposer les paramètres utiles directement dans la carte du canvas ; utiliser une section avancée repliable pour les réglages secondaires.
- Chaque instance d'action garde ses propres paramètres. Les enregistrer avec le brouillon et les restaurer lors des changements d'automatisation et au redémarrage.
- Interagir avec un champ ou une liste dans la carte ne doit pas déplacer la carte ni créer de liaison.
- Préserver la palette : Twitch mauve, OBS noir/gris, CORE bleu, avec texte contrasté.
- Distinguer la configuration et la validation d'une action de son exécution réelle. Ne pas annoncer une exécution implémentée sans vérification.
