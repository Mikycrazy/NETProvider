﻿/*
 *    The contents of this file are subject to the Initial
 *    Developer's Public License Version 1.0 (the "License");
 *    you may not use this file except in compliance with the
 *    License. You may obtain a copy of the License at
 *    https://github.com/FirebirdSQL/NETProvider/blob/master/license.txt.
 *
 *    Software distributed under the License is distributed on
 *    an "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, either
 *    express or implied. See the License for the specific
 *    language governing rights and limitations under the License.
 *
 *    All Rights Reserved.
 */

//$Authors = Jiri Cincura (jiri@cincura.net), Jean Ressouche, Rafael Almeida (ralms@ralms.net)

using System;
using System.Linq;
using FirebirdSql.EntityFrameworkCore.Firebird.Infrastructure.Internal;
using FirebirdSql.EntityFrameworkCore.Firebird.Metadata;
using FirebirdSql.EntityFrameworkCore.Firebird.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace FirebirdSql.EntityFrameworkCore.Firebird.Migrations
{
	public class FbMigrationsSqlGenerator : MigrationsSqlGenerator
	{
		readonly IFbMigrationSqlGeneratorBehavior _behavior;
		readonly IFbOptions _options;

		public FbMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies, IFbMigrationSqlGeneratorBehavior behavior, IFbOptions options)
			: base(dependencies)
		{
			_behavior = behavior;
			_options = options;
		}

		protected override void Generate(CreateTableOperation operation, IModel model, MigrationCommandListBuilder builder, bool terminate)
		{
			base.Generate(operation, model, builder, false);
			if (terminate)
			{
				builder.Append(Dependencies.SqlGenerationHelper.StatementTerminator);
				EndStatement(builder);

				var columns = operation.Columns.Where(p => !p.IsNullable && string.IsNullOrWhiteSpace(p.DefaultValueSql) && p.DefaultValue == null);
				foreach (var column in columns)
				{
					var valueGenerationStrategy = column[FbAnnotationNames.ValueGenerationStrategy] as FbValueGenerationStrategy?;
					if (valueGenerationStrategy == FbValueGenerationStrategy.SequenceTrigger)
					{
						_behavior.CreateSequenceTriggerForColumn(column.Name, column.Table, column.Schema, builder);
					}
				}
			}
		}

		protected override void Generate(AlterColumnOperation operation, IModel model, MigrationCommandListBuilder builder)
		{
			var valueGenerationStrategy = operation[FbAnnotationNames.ValueGenerationStrategy] as FbValueGenerationStrategy?;
			var oldValueGenerationStrategy = operation.OldColumn[FbAnnotationNames.ValueGenerationStrategy] as FbValueGenerationStrategy?;
			if (oldValueGenerationStrategy == FbValueGenerationStrategy.IdentityColumn && valueGenerationStrategy != FbValueGenerationStrategy.IdentityColumn)
			{
				throw new InvalidOperationException("Cannot remove identity on < FB4.");

				// will be recreated, if needed, by next statement
				// supported only on FB4
				//builder.Append("ALTER TABLE ");
				//builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema));
				//builder.Append(" ALTER COLUMN ");
				//builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
				//builder.Append(" DROP IDENTITY");
				//builder.Append(Dependencies.SqlGenerationHelper.StatementTerminator);
				//EndStatement(builder);
			}
			if (oldValueGenerationStrategy == FbValueGenerationStrategy.SequenceTrigger && valueGenerationStrategy != FbValueGenerationStrategy.SequenceTrigger)
			{
				_behavior.DropSequenceTriggerForColumn(operation.Name, operation.Table, operation.Schema, builder);
			}

			// will be recreated, if needed, by next statement
			builder.Append("ALTER TABLE ");
			builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema));
			builder.Append(" ALTER COLUMN ");
			builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
			builder.Append(" DROP NOT NULL");
			builder.Append(Dependencies.SqlGenerationHelper.StatementTerminator);
			EndStatement(builder);

			builder.Append("ALTER TABLE ");
			builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema));
			builder.Append(" ALTER COLUMN ");
			builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
			builder.Append(" TYPE ");
			if (operation.ColumnType != null)
			{
				builder.Append(operation.ColumnType);
			}
			else
			{
				var type = GetColumnType(operation.Schema, operation.Table, operation.Name, operation.ClrType, operation.IsUnicode, operation.MaxLength, operation.IsFixedLength, operation.IsRowVersion, model);
				builder.Append(type);
			}
			if (valueGenerationStrategy == FbValueGenerationStrategy.IdentityColumn)
			{
				builder.Append(" GENERATED BY DEFAULT AS IDENTITY");
			}
			builder.Append(operation.IsNullable ? string.Empty : " NOT NULL");
			builder.Append(Dependencies.SqlGenerationHelper.StatementTerminator);
			EndStatement(builder);

			if (operation.DefaultValue != null || !string.IsNullOrWhiteSpace(operation.DefaultValueSql))
			{
				builder.Append("ALTER TABLE ");
				builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema));
				builder.Append(" ALTER COLUMN ");
				builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
				builder.Append(" DROP DEFAULT");
				builder.Append(Dependencies.SqlGenerationHelper.StatementTerminator);
				EndStatement(builder);


				builder.Append("ALTER TABLE ");
				builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema));
				builder.Append(" ALTER COLUMN ");
				builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
				builder.Append(" SET");
				DefaultValue(operation.DefaultValue, operation.DefaultValueSql, builder);
				builder.Append(Dependencies.SqlGenerationHelper.StatementTerminator);
				EndStatement(builder);
			}

			if (valueGenerationStrategy == FbValueGenerationStrategy.SequenceTrigger)
			{
				_behavior.CreateSequenceTriggerForColumn(operation.Name, operation.Table, operation.Schema, builder);
			}
		}

		protected override void ColumnDefinition(string schema, string table, string name, Type clrType, string type, bool? unicode, int? maxLength, bool? fixedLength, bool rowVersion, bool nullable, object defaultValue, string defaultValueSql, string computedColumnSql, IAnnotatable annotatable, IModel model, MigrationCommandListBuilder builder)
		{
			builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
				   .Append(" ")
				   .Append(type ?? GetColumnType(schema, table, name, clrType, unicode, maxLength, fixedLength, rowVersion, model));

			var valueGenerationStrategy = annotatable[FbAnnotationNames.ValueGenerationStrategy] as FbValueGenerationStrategy?;
			if (valueGenerationStrategy == FbValueGenerationStrategy.IdentityColumn)
			{
				builder.Append(" GENERATED BY DEFAULT AS IDENTITY");
			}

			if (!nullable)
			{
				builder.Append(" NOT NULL");
			}

			DefaultValue(defaultValue, defaultValueSql, builder);
		}

		protected override void ForeignKeyAction(ReferentialAction referentialAction, MigrationCommandListBuilder builder)
		{
			switch (referentialAction)
			{
				case ReferentialAction.Restrict:
					builder.Append("NO ACTION");
					break;
				default:
					base.ForeignKeyAction(referentialAction, builder);
					break;
			}
		}
	}
}
