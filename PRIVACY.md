# Politique de confidentialité — StarXelem

**Dernière mise à jour : 22 juillet 2026**

StarXelem est un logiciel gratuit et open source, développé et maintenu par Grégory Compte (chibiketan). Cette politique décrit précisément quelles données l'application manipule, où elles vont, et où elles ne vont pas.

## Résumé

StarXelem ne collecte, ne stocke sur un serveur, ni ne vend aucune donnée personnelle. L'application fonctionne entièrement en local sur votre machine, à l'exception des échanges nécessaires avec les serveurs de Cloud Imperium Games (CIG) pour afficher vos propres données de jeu.

## Données lues localement

StarXelem lit et traite localement, sur votre machine, les éléments suivants :

- Le fichier `Game.log` de Star Citizen, pour extraire les événements de votre session de jeu (journal de session).
- Un fichier créé par le jeu lui-même sur votre machine, contenant un jeton d'authentification (JWT) associé à votre session de jeu en cours.
- Les fichiers de données du jeu (P4K) et/ou une base de données SQLite locale embarquée avec l'application, utilisés pour afficher les informations de blueprints, objets et réputation.

## Données transmises à Cloud Imperium Games (CIG)

Pour afficher vos propres données de jeu (réputation, blueprints, objets), StarXelem initie une session auprès des API officielles de Cloud Imperium Games (CIG), l'éditeur de Star Citizen, en utilisant :

- **Le jeton d'authentification (JWT)** lu depuis le fichier créé par le jeu, transmis à CIG pour initier la session — de la même façon que le ferait le jeu lui-même ou le site officiel RSI/Hangar pour s'authentifier auprès de ses propres serveurs.
- **L'identifiant de compte** renvoyé dans les informations de session, transmis avec la majorité des requêtes suivantes afin que CIG retourne les données correspondant à votre compte.

**Ce que StarXelem fait de ce jeton :** il est conservé uniquement en mémoire vive (RAM) le temps de l'exécution de l'application, et n'est jamais écrit sur le disque ni stocké dans un fichier de configuration ou une base de données locale. Il est effacé de la mémoire à la fermeture de l'application. Il n'est transmis à aucune destination autre que les points d'accès officiels de l'API de CIG.

StarXelem ne fait qu'agir comme intermédiaire d'affichage entre vous et les serveurs de CIG : l'application elle-même ne conserve, ne journalise et ne transmet ce jeton ou cet identifiant de compte à aucun autre tiers.

L'usage de ces API et les données qui y transitent restent soumis à la politique de confidentialité de Cloud Imperium Games, disponible sur [robertsspaceindustries.com](https://robertsspaceindustries.com/).

## Ce que StarXelem ne fait pas

- Pas de collecte de données personnelles (nom, e-mail, adresse IP, identifiants) à des fins autres que l'interrogation des API CIG décrite ci-dessus.
- Pas de télémétrie, d'analytics, ni de suivi d'utilisation.
- Pas de compte utilisateur, pas d'inscription, pas de mot de passe à fournir à StarXelem.
- Pas de publicité, pas de revente ou partage de données à des tiers commerciaux.
- Aucune donnée n'est envoyée vers une infrastructure serveur appartenant à StarXelem ou à son auteur : il n'existe pas de serveur StarXelem.

## Stockage local

Les données de configuration et le cache local de l'application (base SQLite, préférences) sont stockés uniquement sur votre machine, dans les dossiers standards Windows (`%LOCALAPPDATA%` ou équivalent). Vous pouvez les supprimer à tout moment en désinstallant l'application ou en supprimant manuellement ces dossiers.

## Code source ouvert

StarXelem est un projet open source. Le code source complet est consultable publiquement sur GitHub : [github.com/chibiketan/StarXelem](https://github.com/chibiketan/StarXelem). Chacun peut vérifier par lui-même l'exactitude des informations ci-dessus en consultant le code.

## Contact

Pour toute question concernant cette politique ou le fonctionnement de l'application, vous pouvez ouvrir une *issue* sur le dépôt GitHub du projet : [github.com/chibiketan/StarXelem/issues](https://github.com/chibiketan/StarXelem/issues).

## Évolution de cette politique

Si le fonctionnement de StarXelem venait à changer (par exemple l'ajout d'une fonctionnalité de télémétrie optionnelle ou d'un service en ligne), cette politique serait mise à jour en conséquence avant la publication de la version concernée, et la date de mise à jour ci-dessus serait modifiée.