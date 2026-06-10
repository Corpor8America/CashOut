# Application Versioning

## Overview
This feature centralizes the application version in a single authoritative location so that all parts of the system — Docker images, CI pipelines, backend APIs, and the UI — consistently reference the same version number.

## Objective
Maintain a unified version source to ensure:

- Docker images always embed the correct version  
- CI can verify that a version bump occurred before deployment  
- The UI and API display the same version consistently  

## Version Storage
The application version is stored in one of the following:

- A root-level `VERSION` file  
- A dedicated `appsettings.version.json` file  

This file acts as the single source of truth for all build and runtime components.

## CI Pipeline Behavior
The CI pipeline performs the following actions:

1. Reads the version file during the build process  
2. Injects the version into:
   - Docker image labels  
   - Build metadata  
3. Fails deployment if the version has not changed for a release build  

This ensures every release is explicitly versioned.

## Backend Behavior
The backend exposes a `/version` endpoint that returns the current application version.  
This allows:

- Health checks  
- UI display  
- Debugging  
- Automated monitoring  

## UI Behavior
The UI displays the current application version in a visible but unobtrusive location, such as:

- A footer  
- A settings screen  
- An “About” dialog  

This ensures users and developers can always confirm which version is running.

