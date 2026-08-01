# Glossary

Use these terms consistently in code, documentation and UI.

## Agent

A small JulOS process installed on a target host. It connects outbound to Server and exposes only configured typed capabilities.

## Application

A launchable JulOS desktop experience registered by Core or a package. An application can have multiple launch targets and windows.

## Application mode

A Browser session opened with a fixed website and reduced browser chrome. It remains a real browser session.

## Capability

A versioned operation contract that a package or Agent can provide and another package can request through Core.

## Capability broker

The Core service that authorizes a capability request and resolves a healthy compatible provider.

## Connection

Stored configuration that allows a package to access an external product, host or file provider. Credentials are referenced through Secret Reference.

## Core

The product-independent JulOS platform behavior: users, permissions, packages, applications, layouts, sessions, Agents, problems and audit metadata.

## Desktop

The browser client shell containing taskbar, launcher, windows, widgets, notifications and problem center.

## Discovery observation

Evidence that a device, service or application may exist. An observation does not grant access or management.

## Launch target

A specific resource or configured destination opened through an Application, such as one discovered web service or one VM console.

## Package

A signed installable JulOS feature unit that can contribute applications, widgets, workers, settings and capabilities.

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

The JulOS ASP.NET Core control plane serving APIs, Desktop assets, authentication, package coordination and Agent connections.

## Session

A live or reconnectable runtime interaction such as Browser, RDP, VNC or SSH. Session lifecycle is separate from window lifecycle.

## Stable external identity

A package-defined identity that continues across ephemeral resource recreation. A Docker container ID is not a stable application identity.

## Widget

A small desktop summary registered by a package. A widget is not a complete management application.

## Window

Saved presentation state for one Application instance, including position, size, state and optional Session reference.

## Viewport class

One of desktop, tablet or mobile. Layouts are stored separately per viewport class.
