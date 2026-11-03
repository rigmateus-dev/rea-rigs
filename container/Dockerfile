# REA de Rig's : la boite fermee ou tourne REA.
# - version exacte de rea-agents (rea-version.txt), dependances figees par son npm-shrinkwrap.json
# - --ignore-scripts : aucun script d'installation n'est execute
# - utilisateur sans droits (node), pas de shell de connexion
# L'isolement (pas de reseau, systeme de fichiers en lecture seule, un seul dossier visible)
# est impose au lancement par l'appli (voir app/src/Docker.cs), pas ici.
FROM node:24.22.1-bookworm-slim

ARG REA_VERSION
RUN test -n "$REA_VERSION" \
 && npm install -g --ignore-scripts --no-audit --no-fund "rea-agents@${REA_VERSION}" \
 && npm cache clean --force \
 && rea --version

LABEL org.opencontainers.image.title="REA de Rig's" \
      org.opencontainers.image.source="https://github.com/rigmateus-dev/rea-rigs" \
      rea.version="${REA_VERSION}"

USER node
ENV HOME=/tmp NODE_ENV=production
WORKDIR /out
ENTRYPOINT ["rea"]
