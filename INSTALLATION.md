# SIMKL Watched

Variante du plugin officiel SIMKL v8. DLL .NET 9, base Jellyfin 10.11.7.

## Comportement

- Le bouton standard « Vu » de Jellyfin envoie le film ou l'épisode à SIMKL.
- Déclencheur : `UserDataSaved`, motif `TogglePlayed`, `Played == true`.
- Les mises à jour de position, fins de lecture automatiques et « Non vu » sont ignorées.
- Les marquages d'épisodes générés par un marquage de saison/série sont traités individuellement ; le conteneur lui-même est ignoré.
- Films : identifiants du film. Épisodes : identifiants de la série + saison + épisode.
- File persistante dans le dossier des configurations des plugins, sans jeton dans cette file.
- Échecs : nouvelles tentatives espacées de 2 minutes à 1 heure. Un identifiant introuvable nécessite de corriger les métadonnées ; une autorisation expirée nécessite de reconnecter SIMKL.
- Un nouveau marquage manuel remplace la demande en attente et annule son délai de nouvelle tentative. Si un envoi est déjà en cours, sa réponse ne peut pas effacer le nouveau clic. Les requêtes HTTP restent séquentielles. Après confirmation, la demande est supprimée : un nouveau clic « Vu » permet un nouvel envoi. Pas de revisionnage ni de suppression de l'historique SIMKL.
- La configuration SIMKL reste côté serveur, comme dans l'officiel (elle n'est pas chiffrée par cette variante).
- La nouvelle identité impose une connexion PIN initiale. La gestion du plugin nécessite un compte administrateur Jellyfin.

La sauvegarde de reprise de ton Lua reste indépendante. Son `Played = false` peut remettre un média en non-vu dans Jellyfin si mpv continue après ton clic : il faudra retirer cette écriture de statut du Lua.

## Héberger plus tard sur GitHub

1. Créer un dépôt GitHub public vide.
2. Y pousser le contenu de ce projet, licence comprise. Ne pas envoyer `bin`, `obj`, `dist` ni les fichiers personnels de configuration.
3. Activer GitHub Actions si nécessaire.
4. Créer et pousser le tag `watched-v1.0.0`.
5. Le workflow `Publish SIMKL Watched` compile, exécute les tests, puis publie une release avec le ZIP et `manifest.json`.

URL stable à ajouter dans Jellyfin, en remplaçant les deux valeurs :

```text
https://github.com/UTILISATEUR/DEPOT/releases/latest/download/manifest.json
```

Le ZIP référencé par chaque manifeste utilise une URL de release versionnée, pas une URL flottante. Le manifeste contient son empreinte MD5, demandée par Jellyfin.

## Installer sur Ultra.cc

1. Dans Jellyfin, désinstaller le plugin officiel SIMKL puis redémarrer l'application pour arrêter son scrobbling.
2. Tableau de bord → Plugins → Dépôts : ajouter l'URL ci-dessus.
3. Ouvrir le catalogue et installer **Simkl Watched**.
4. Redémarrer Jellyfin depuis les commandes ou le panneau Ultra.cc habituels.
5. Configurer **Simkl Watched**, sélectionner l'utilisateur, connecter SIMKL par PIN et activer films/épisodes.
6. Sur un film ou épisode identifié, cliquer « Vu ». Vérifier l'historique SIMKL et la ligne `SIMKL watched confirmed` dans les journaux.

Aucun accès SSH à `/config` n'est nécessaire. La possibilité d'ajouter un dépôt tiers, de télécharger ses fichiers et de redémarrer doit fonctionner dans ton instance ; elle n'a pas été testée sur ta seedbox.

## Corrections suivantes

Modifier les trois versions de `Directory.Build.props` (exemple `1.0.1.0`), pousser le code puis le tag `watched-v1.0.1`. Le workflow publie une nouvelle release. Mettre à jour le plugin dans Jellyfin puis redémarrer.

Ne pas réutiliser un numéro de version pour un ZIP différent.

## Compilation locale

Installer le SDK .NET 9 et Python 3, puis :

```sh
dotnet build Jellyfin.Plugin.Simkl/Jellyfin.Plugin.Simkl.csproj -c Release
dotnet run --project tests/Checks.csproj -c Release
python package.py --base-url https://github.com/UTILISATEUR/DEPOT/releases/download/watched-v1.0.0
```

Le script crée `dist/SimklWatched_1.0.0.0.zip` et `dist/manifest.json`. La DLL est portable Windows/Linux sous Jellyfin compatible.

## Validation et limites

Compilation avec analyseurs : zéro erreur, zéro avertissement. Tests locaux avec transport HTTP simulé : filtrage des événements, payloads film/épisode, réponses inconnues, erreurs HTTP. Aucune requête réelle de marquage n'a été envoyée à SIMKL. Le chargement dans Jellyfin, la connexion PIN et le cycle complet sur Ultra.cc restent à valider après publication.

La file conserve uniquement les demandes en attente. Les anciennes confirmations de la version 1.0 sont purgées automatiquement au démarrage, sans perdre les demandes en attente. Après une suppression côté SIMKL, passer à « Non vu » puis « Vu » dans Jellyfin permet de renvoyer le média. Une coupure entre l'acceptation distante et l'enregistrement local peut provoquer une nouvelle tentative : aucune garantie d'exactement un envoi réseau n'est possible sans idempotence distante.

La file n'importe pas les anciens médias déjà vus avant installation. Une remise à « Non vu » après un clic ne supprime pas la demande déjà mise en file : l'intégration est volontairement à sens unique.

## Provenance

Source : https://github.com/jellyfin/jellyfin-plugin-simkl/tree/v8
Licence GPL-3.0 conservée. Variante indépendante, non officielle.
