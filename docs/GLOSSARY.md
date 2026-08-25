# Glossary

Use these terms consistently in code, documentation and UI.

## App installation

One selected catalog delivery connected to or deployed on one target. It has a stable JulOS identity and managed, adopted or external ownership.

## Application catalog

A versioned source of application metadata and delivery options. It may be official, community-managed, Git/HTTPS/OCI-backed or local.

## Background mode

The user preference `suspend` or `keep-surface-active` for an application's hidden frontend Surface. Keep-surface-active is best effort and is not durable background execution; Remote Session `keep-active` is a different lifecycle contract.

## Client device

One explicitly registered browser/PWA installation used to resolve layout preferences. It is random, user-scoped and never an authentication credential.

## Container terminal

A short-lived, permission-checked Remote terminal session attached by the Docker package to one selected container. It is not a Host Connector or host shell.

## Application

A launchable JulOS desktop experience registered by Core or a package. An application can have multiple launch targets and windows.

## Application mode

A Browser session opened with a fixed website and reduced browser chrome. It remains a real browser session.

## Capability

A versioned operation contract that a package or Host Connector can provide and another package can request through Core.

## Capability broker

The Core service that authorizes a capability request and resolves a healthy compatible provider.

## Connection

Stored configuration that allows a package to access an external product, host or file provider. Credentials are referenced through Secret Reference.

## Core

The product-independent JulOS platform behavior: users, permissions, extension packages, applications, layouts, sessions, Host Connectors, problems and audit metadata.

## Desktop

The browser client shell containing taskbar, launcher, windows, widgets, notifications and problem center.

## Discovery observation

Evidence that a device, service or application may exist. An observation does not grant access or management.

## Host Connector

An optional small JulOS host service installed when a package needs local host resources. It connects outbound with a durable enrolled identity and exposes only configured, versioned typed capabilities. It has no assistant/chat role, package UI, package business logic or general host shell.

`Agent` is the legacy pre-migration name retained only in historical work items, releases, migrations and compatibility documentation.

## Layout scope

`shared` uses the user's workspace-class layout across devices. `device` uses a layout owned by one registered Client device. `fresh` is a restore mode that persists no Window state.

## Launch target

A specific resource or configured destination opened through an Application, such as one discovered web service or one VM console.

## Package

An installable JulOS extension unit that can contribute applications, widgets, workers, settings and capabilities. Its integrity digest is mandatory; publisher signature state is separately visible.

## Package worker

An out-of-process backend service for one enabled package.

## Problem

A persistent deduplicated operational condition that has source, resource identity, severity, state and suggested action.

## Remote

The JulOS package providing protocol-neutral remote-session orchestration and RDP, VNC, SSH and console adapters.

## Runtime

An isolated process or container created to perform a session function, such as Chromium or a remote protocol service.

## Runtime Manager

A narrow privileged control-plane sidecar that manages only JulOS-owned runtime containers. It is not the Docker package.

## Secret Reference

An opaque identifier for encrypted credential material. The value is never returned to normal frontend APIs.

## Server

The JulOS ASP.NET Core control plane serving APIs, Desktop assets, authentication, package/application coordination and Host Connector connections.

## Session

A live or reconnectable runtime interaction such as Browser, RDP, VNC or SSH. Session lifecycle is separate from window lifecycle.

## Stable external identity

A package-defined identity that continues across ephemeral resource recreation. A Docker container ID is not a stable application identity.

## Surface

One package frontend instance hosted for a Window. Its foreground-focused, foreground-visible, background-active, suspended, faulted or terminated execution state is separate from Window presentation and runtime Session state.

## Widget

A small desktop summary registered by a package. A widget is not a complete management application.

## Window

Saved presentation state for one Application instance, including position, size, state and optional Session reference.

## Viewport class

One of desktop, tablet or mobile declared for application compatibility. Persisted presentation uses Workspace class instead.

## Workspace class

One of Phone, Tablet, desktop-single or desktop-multi. Shared and optional device-scoped layouts are stored separately per Workspace class.
