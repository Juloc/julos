# JulOS Reference Package

This official package exercises the JulOS 1.0 package platform without containing product-specific infrastructure logic.

It includes:

- one single-instance application
- one small or medium widget
- one package worker
- one provided capability (`reference.echo`)
- configuration validation
- one reversible package-database migration declaration
- an intentional worker health fault mode
- a signed frontend module contract

The release pipeline builds the worker, stages the declared files, verifies the frontend digest, creates the immutable package archive and signs the artifact with the official package publisher key.

`faultMode=true` is only for validating fault isolation, problem creation, safe disable and recovery behavior.
