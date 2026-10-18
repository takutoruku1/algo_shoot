---
name: export-build
description: Build and package this Godot/.NET game as a distributable single Windows exe and a zip. Use when the user asks to export the exe, output/rebuild the release build, or make a zip to hand out (e.g. "exeに出力して", "exe出力しなおして", "zipにまとめて", "配布用ビルド").
---

# export-build — Windows exe + zip 書き出し

実行前に、プロジェクト直下の [AGENTS.md](../../../AGENTS.md) にある「Windows exe・配布zipの出力ルール」を必ず全文読む。版番号、命名、説明書、単体exe、zip、検証、報告の規則はそこを唯一の正本とする。

このスキルからのみ呼ばれた場合も、AGENTS.mdを読まずに書き出さない。ルールを変更するときはAGENTS.mdを更新し、ここに別の命名例や固定の現在バージョンを重複して持たせない。

エディタがPATHに無ければ、インストール済みのGodot .NETエディタを探してバージョンを確認する。既存のexport_presets.cfgと同じ版のテンプレートを使用し、正本に記載された順序で版更新・ビルド・インポート・書き出し・起動確認・zip作成を完了する。
