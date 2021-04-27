using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace sql_prom_exporter
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services){}

        private const string ConnectionString = ""; // TODO make this or something like this work Environment.GetEnvironmentVariable("ConnectionStrings: default")))
        static string[] sqlStatements;
        static Gauge[] gauges;



        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Hello World!");
                });
            });

            ConfigureSqlStatements();
            ConfigureGauges();

            Metrics.DefaultRegistry.AddBeforeCollectCallback(async (cancel) =>
            {
                await QueryMetrics(cancel);
            });
        }

        private static async Task QueryMetrics(CancellationToken cancel)
        {
            Parallel.For(0, sqlStatements.Length, new ParallelOptions { MaxDegreeOfParallelism = 1 /* TODO  config me */ }, (i) =>
            {
                using SqlConnection connection = new SqlConnection(ConnectionString);
                connection.Open();
                string sql = sqlStatements[i];
                SqlCommand command = connection.CreateCommand();
                command.CommandText = sql;
                using SqlDataReader reader = await command.ExecuteReaderAsync(cancel); //TODO run all statements concurently?
                bool first = true;
                int colCount = 0;

                while (await reader.ReadAsync(cancel))
                {
                    if (first) colCount = reader.FieldCount;
                    double value = Convert.ToDouble(reader[0]);
                    string[] labels = new string[colCount - 1];
                    for (int j = 1; j < colCount; j++)
                    {
                        labels[j - 1] = reader.GetString(j);
                    }
                    gauges[i].WithLabels(labels).Set(value);
                }
            });
        }

        private static void ConfigureSqlStatements()
        {
            sqlStatements = new[] {
                "SELECT 1 [sql_metric_one], 'labelValue' [labelName]",
                "SELECT cntr_value [sql_page_life_expectancy_seconds] FROM sys.dm_os_performance_counters WHERE [counter_name] = 'Page life expectancy' AND object_name LIKE '%buffer manager%'",
                @"  SELECT cast(dm_os_performance_counters.cntr_value as money) / RatioBase.cntr_value * 100 as 'sql_buffer_cache_hit_percent'
FROM sys.dm_os_performance_counters
JOIN (
    SELECT instance_name, cntr_value
    FROM sys.dm_os_performance_counters
    WHERE object_name LIKE '%buffer manager%' AND counter_name = 'Buffer cache hit ratio base'
) as RatioBase on RatioBase.instance_name = dm_os_performance_counters.instance_name
WHERE object_name LIKE '%buffer manager%'
    AND counter_name = 'Buffer cache hit ratio'",
            }; //TODO lookup from somewhere, different defaults in different images, optional configmap to lookup more, null image with none
        }

        /*
         * select
    schema_name(o.Schema_ID) [Schema],
    o.name [Object],
    i.name [Index],
    oip.pages * 8 [Buffer KB],
    convert(decimal(9, 2), (convert(real, oip.pages) / oiu.used_pages) * 100) [% of Used Pages],
    convert(decimal(9, 2), (convert(real, oip.pages) / oiu.total_pages) * 100) [% of Total Pages]
from sys.objects o
join sys.indexes i on o.object_id = i.object_id
join (
    select
        a.object_id,
        a.index_id,
        count(*) pages
    from sys.dm_os_buffer_descriptors b
    join (
        select object_id, index_id, allocation_unit_id
        from sys.allocation_units au
        join sys.partitions p on au.container_id = p.hobt_id
            and au.type in ( 1, 3 )
        union all
        select object_id, index_id, allocation_unit_id
        from sys.allocation_units au
        join sys.partitions p on au.container_id = p.partition_id
            and au.type = 2
        ) a on a.allocation_unit_id = b.allocation_unit_id
    where database_id = db_id()
    group by object_id, index_id
) oip on o.object_id = oip.object_id
    and i.index_id = oip.index_id
join (
    select object_id, index_id, sum(used_pages) used_pages, sum(total_pages) total_pages
    from (
    select object_id, index_id ,allocation_unit_id, used_pages, total_pages
    from sys.allocation_units au
    join sys.partitions p on au.container_id = p.hobt_id
        and au.type in ( 1, 3 )
    union all
    select object_id, index_id, allocation_unit_id, used_pages, total_pages
    from sys.allocation_units au
    join sys.partitions p on au.container_id = p.partition_id
        and au.type = 2
    ) oiu
    group by object_id, index_id
    having sum(used_pages) <> 0
) oiu on o.object_id = oiu.object_id
    and i.index_id = oiu.index_id
--where o.is_ms_shipped = 0
order by pages desc, o.name, i.name




select
    isnull(db_name(database_id), concat('database_id ', database_id)) [Database],
    count(*) * 8 [Buffer KB]
from sys.dm_os_buffer_descriptors
group by database_id
order by count(*) desc

        */


        private static void ConfigureGauges()
        {
            gauges = new Gauge[sqlStatements.Length];

            using (SqlConnection connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                for (int i = 0; i < sqlStatements.Length; i++)
                {
                    string sql = sqlStatements[i];
                    SqlCommand command = connection.CreateCommand();
                    command.CommandText = sql;
                    using (SqlDataReader sqlDataReader = command.ExecuteReader())
                    {
                        sqlDataReader.Read();

                        List<string> columnNames = sqlDataReader.GetColumnSchema().Select(col => col.ColumnName).ToList();

                        gauges[i] = Metrics.CreateGauge(columnNames[0], "", columnNames.Skip(1).ToArray());
                    }
                }
            }
        }
    }
}
