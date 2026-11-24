using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace McpToolkit;

[McpServerToolType]
public sealed class WordPressTools(WordPressClient wp)
{
    private static readonly HttpMethod PatchMethod = new("PATCH");

    // ---------- Site ----------

    [McpServerTool(Title = "Informações do site", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Retorna as informações públicas do site WordPress (nome, descrição, URL, fuso horário, namespaces e rotas disponíveis da REST API). Use para confirmar que a conexão funciona e descobrir quais plugins expõem rotas. As credenciais vêm do header x-api-key (ou da configuração do servidor).")]
    public async Task<string> site_info(
        [Description("Se true, devolve também a lista completa de rotas (pode ser grande). Padrão false: só nome, descrição, URL, fuso e namespaces")] bool include_routes = false,
        CancellationToken ct = default)
    {
        var response = await wp.GetAsync(string.Empty, null, ct);
        if (include_routes || response.Data is not JsonObject site)
            return WordPressArgs.Result(response);

        return new JsonObject
        {
            ["name"] = site["name"]?.DeepClone(),
            ["description"] = site["description"]?.DeepClone(),
            ["url"] = site["url"]?.DeepClone(),
            ["home"] = site["home"]?.DeepClone(),
            ["gmt_offset"] = site["gmt_offset"]?.DeepClone(),
            ["timezone_string"] = site["timezone_string"]?.DeepClone(),
            ["namespaces"] = site["namespaces"]?.DeepClone(),
            ["authentication"] = site["authentication"]?.DeepClone()
        }.ToJsonString();
    }

    [McpServerTool(Title = "Usuário autenticado", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Retorna o usuário WordPress dono das credenciais em uso (id, nome, e-mail, papéis e capacidades). Útil para saber o que as credenciais podem fazer.")]
    public async Task<string> get_current_user(CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.GetAsync("wp/v2/users/me", WordPressArgs.Query(("context", "edit")), ct));

    // ---------- Posts e páginas ----------

    [McpServerTool(Title = "Listar posts/páginas", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Lista posts, páginas ou itens de um tipo customizado, com filtros e paginação. Retorna {total, totalPages, items}. Para rascunhos e privados use status='any' (exige credenciais com permissão de edição). Os itens usam context=edit, trazendo também o conteúdo bruto (content.raw).")]
    public async Task<string> list_posts(
        [Description("Tipo (rest_base): 'posts' (padrão), 'pages' ou o rest_base de um tipo customizado")] string post_type = "posts",
        [Description("Texto de busca (opcional)")] string? search = null,
        [Description("Status: publish, draft, pending, private, future, trash ou 'any' (opcional; padrão do WordPress: publish)")] string? status = null,
        [Description("Ids de categorias separados por vírgula (opcional, só posts)")] string? categories = null,
        [Description("Ids de tags separados por vírgula (opcional, só posts)")] string? tags = null,
        [Description("Id do autor (opcional)")] int? author = null,
        [Description("Slug exato (opcional)")] string? slug = null,
        [Description("Campo de ordenação: date (padrão), modified, title, id, relevance, slug")] string? orderby = null,
        [Description("Direção: asc ou desc (padrão desc)")] string? order = null,
        [Description("Itens por página (1 a 100, padrão 10)")] int per_page = 10,
        [Description("Número da página (padrão 1)")] int page = 1,
        CancellationToken ct = default)
    {
        var restBase = WordPressArgs.RestBase(post_type, "posts");
        var query = WordPressArgs.Query(
            ("context", "edit"), ("search", search), ("status", status), ("categories", categories), ("tags", tags),
            ("author", author), ("slug", slug), ("orderby", orderby), ("order", order),
            ("per_page", Math.Clamp(per_page, 1, 100)), ("page", Math.Max(page, 1)));
        return WordPressArgs.ListResult(await wp.GetAsync($"wp/v2/{restBase}", query, ct));
    }

    [McpServerTool(Title = "Obter post/página", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Retorna um post, página ou item de tipo customizado pelo id, com o conteúdo bruto (context=edit).")]
    public async Task<string> get_post(
        [Description("Id do item")] int id,
        [Description("Tipo (rest_base): 'posts' (padrão), 'pages' ou tipo customizado")] string post_type = "posts",
        CancellationToken ct = default)
    {
        var restBase = WordPressArgs.RestBase(post_type, "posts");
        return WordPressArgs.Result(await wp.GetAsync($"wp/v2/{restBase}/{id}", WordPressArgs.Query(("context", "edit")), ct));
    }

    [McpServerTool(Title = "Criar post/página", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true), Description("Cria um post, página ou item de tipo customizado. O status padrão é 'draft' (rascunho) por segurança; use status='publish' para publicar. O conteúdo aceita HTML (ou blocos Gutenberg). Campos adicionais (meta, template, format, sticky...) podem ir em extra_json.")]
    public async Task<string> create_post(
        [Description("Título")] string title,
        [Description("Conteúdo em HTML (opcional)")] string? content = null,
        [Description("Status: draft (padrão), publish, pending, private ou future")] string status = "draft",
        [Description("Resumo (opcional)")] string? excerpt = null,
        [Description("Slug (opcional)")] string? slug = null,
        [Description("Ids de categorias separados por vírgula (opcional, só posts)")] string? categories = null,
        [Description("Ids de tags separados por vírgula (opcional, só posts)")] string? tags = null,
        [Description("Id da mídia de imagem destacada (opcional)")] int? featured_media = null,
        [Description("Id do autor (opcional; padrão: usuário autenticado)")] int? author = null,
        [Description("Id da página pai (opcional, só páginas)")] int? parent = null,
        [Description("Data de publicação ISO 8601 no fuso do site, ex.: 2026-12-01T09:00:00 (opcional; com status=future agenda)")] string? date = null,
        [Description("Tipo (rest_base): 'posts' (padrão), 'pages' ou tipo customizado")] string post_type = "posts",
        [Description("Objeto JSON com campos extras da REST API (opcional), ex.: {\"meta\":{\"chave\":\"valor\"}}")] string? extra_json = null,
        CancellationToken ct = default)
    {
        var restBase = WordPressArgs.RestBase(post_type, "posts");
        var body = WordPressArgs.Body(
            ("title", WordPressArgs.Str(title)), ("content", WordPressArgs.Str(content)), ("status", WordPressArgs.Str(status)),
            ("excerpt", WordPressArgs.Str(excerpt)), ("slug", WordPressArgs.Str(slug)),
            ("categories", WordPressArgs.IdArray(categories)), ("tags", WordPressArgs.IdArray(tags)),
            ("featured_media", WordPressArgs.Num(featured_media)), ("author", WordPressArgs.Num(author)),
            ("parent", WordPressArgs.Num(parent)), ("date", WordPressArgs.Str(date)));
        WordPressArgs.Merge(body, WordPressArgs.ParseObject(extra_json, nameof(extra_json)));
        return WordPressArgs.Result(await wp.SendAsync(HttpMethod.Post, $"wp/v2/{restBase}", null, body, ct));
    }

    [McpServerTool(Title = "Atualizar post/página", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true), Description("Atualiza campos de um post, página ou item de tipo customizado. Só os parâmetros informados são alterados. Atenção: 'content' substitui o conteúdo inteiro — consulte antes com get_post se precisar preservar partes.")]
    public async Task<string> update_post(
        [Description("Id do item")] int id,
        [Description("Novo título (opcional)")] string? title = null,
        [Description("Novo conteúdo em HTML (opcional; substitui o existente)")] string? content = null,
        [Description("Novo status: draft, publish, pending, private ou future (opcional)")] string? status = null,
        [Description("Novo resumo (opcional)")] string? excerpt = null,
        [Description("Novo slug (opcional)")] string? slug = null,
        [Description("Ids de categorias separados por vírgula (opcional; substitui as atuais)")] string? categories = null,
        [Description("Ids de tags separados por vírgula (opcional; substitui as atuais)")] string? tags = null,
        [Description("Id da mídia de imagem destacada (opcional; 0 remove)")] int? featured_media = null,
        [Description("Id do autor (opcional)")] int? author = null,
        [Description("Id da página pai (opcional)")] int? parent = null,
        [Description("Data ISO 8601 no fuso do site (opcional)")] string? date = null,
        [Description("Tipo (rest_base): 'posts' (padrão), 'pages' ou tipo customizado")] string post_type = "posts",
        [Description("Objeto JSON com campos extras da REST API (opcional)")] string? extra_json = null,
        CancellationToken ct = default)
    {
        var restBase = WordPressArgs.RestBase(post_type, "posts");
        var body = WordPressArgs.Body(
            ("title", WordPressArgs.Str(title)), ("content", WordPressArgs.Str(content)), ("status", WordPressArgs.Str(status)),
            ("excerpt", WordPressArgs.Str(excerpt)), ("slug", WordPressArgs.Str(slug)),
            ("categories", WordPressArgs.IdArray(categories)), ("tags", WordPressArgs.IdArray(tags)),
            ("featured_media", WordPressArgs.Num(featured_media)), ("author", WordPressArgs.Num(author)),
            ("parent", WordPressArgs.Num(parent)), ("date", WordPressArgs.Str(date)));
        WordPressArgs.Merge(body, WordPressArgs.ParseObject(extra_json, nameof(extra_json)));
        if (body.Count == 0)
            throw new InvalidOperationException("Informe ao menos um campo para atualizar.");
        return WordPressArgs.Result(await wp.SendAsync(HttpMethod.Post, $"wp/v2/{restBase}/{id}", null, body, ct));
    }

    [McpServerTool(Title = "Excluir post/página", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true), Description("Move um post/página para a lixeira (padrão) ou o exclui permanentemente (force=true, irreversível). Tipos sem lixeira exigem force=true.")]
    public async Task<string> delete_post(
        [Description("Id do item")] int id,
        [Description("Tipo (rest_base): 'posts' (padrão), 'pages' ou tipo customizado")] string post_type = "posts",
        [Description("true = exclusão permanente e irreversível; false (padrão) = lixeira")] bool force = false,
        CancellationToken ct = default)
    {
        var restBase = WordPressArgs.RestBase(post_type, "posts");
        return WordPressArgs.Result(await wp.SendAsync(HttpMethod.Delete, $"wp/v2/{restBase}/{id}",
            WordPressArgs.Query(("force", force)), null, ct));
    }

    // ---------- Mídia ----------

    [McpServerTool(Title = "Listar mídia", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Lista itens da biblioteca de mídia, com filtros e paginação. Retorna {total, totalPages, items}.")]
    public async Task<string> list_media(
        [Description("Texto de busca (opcional)")] string? search = null,
        [Description("Tipo de mídia: image, video, audio ou application (opcional)")] string? media_type = null,
        [Description("Tipo MIME exato, ex.: image/png (opcional)")] string? mime_type = null,
        [Description("Id do post ao qual a mídia está anexada (opcional)")] int? parent = null,
        [Description("Itens por página (1 a 100, padrão 10)")] int per_page = 10,
        [Description("Número da página (padrão 1)")] int page = 1,
        CancellationToken ct = default)
    {
        var query = WordPressArgs.Query(
            ("context", "edit"), ("search", search), ("media_type", media_type), ("mime_type", mime_type), ("parent", parent),
            ("per_page", Math.Clamp(per_page, 1, 100)), ("page", Math.Max(page, 1)));
        return WordPressArgs.ListResult(await wp.GetAsync("wp/v2/media", query, ct));
    }

    [McpServerTool(Title = "Obter mídia", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Retorna um item da biblioteca de mídia pelo id (URL de origem, tamanhos, tipo MIME, texto alternativo).")]
    public async Task<string> get_media(
        [Description("Id da mídia")] int id,
        CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.GetAsync($"wp/v2/media/{id}", WordPressArgs.Query(("context", "edit")), ct));

    [McpServerTool(Title = "Enviar mídia", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true), Description("Envia um arquivo para a biblioteca de mídia. Informe a origem do arquivo por source_url (o servidor baixa a URL http/https) ou por base64 (conteúdo do arquivo em Base64). Retorna o item criado, com o id para usar em featured_media.")]
    public async Task<string> upload_media(
        [Description("Nome do arquivo com extensão, ex.: capa.jpg")] string file_name,
        [Description("URL http/https de onde baixar o arquivo (use este OU base64)")] string? source_url = null,
        [Description("Conteúdo do arquivo em Base64 (use este OU source_url)")] string? base64 = null,
        [Description("Tipo MIME, ex.: image/jpeg (opcional; deduzido da extensão ou da resposta da URL)")] string? mime_type = null,
        [Description("Título (opcional)")] string? title = null,
        [Description("Texto alternativo da imagem (opcional)")] string? alt_text = null,
        [Description("Legenda (opcional)")] string? caption = null,
        [Description("Id do post ao qual anexar (opcional)")] int? post = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(file_name))
            throw new InvalidOperationException("Informe file_name.");
        if (string.IsNullOrWhiteSpace(source_url) == string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException("Informe exatamente um entre source_url e base64.");

        byte[] data;
        var mime = mime_type;
        if (!string.IsNullOrWhiteSpace(base64))
        {
            try
            {
                data = Convert.FromBase64String(base64.Trim());
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("O parâmetro base64 não é um Base64 válido.");
            }
        }
        else
        {
            if (!Uri.TryCreate(source_url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("source_url deve ser uma URL http(s) completa.");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            try
            {
                using var download = await http.GetAsync(uri, ct);
                download.EnsureSuccessStatusCode();
                data = await download.Content.ReadAsByteArrayAsync(ct);
                mime ??= download.Content.Headers.ContentType?.MediaType;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Falha ao baixar source_url: {ex.Message}");
            }
        }

        mime = string.IsNullOrWhiteSpace(mime) ? GuessMime(file_name) : mime;
        var upload = await wp.UploadAsync("wp/v2/media", data, file_name, mime, null, ct);

        var extra = WordPressArgs.Body(
            ("title", WordPressArgs.Str(title)), ("alt_text", WordPressArgs.Str(alt_text)),
            ("caption", WordPressArgs.Str(caption)), ("post", WordPressArgs.Num(post)));
        if (extra.Count == 0 || upload.Data?["id"] is not { } idNode)
            return WordPressArgs.Result(upload);

        var updated = await wp.SendAsync(HttpMethod.Post, $"wp/v2/media/{idNode}", null, extra, ct);
        return WordPressArgs.Result(updated);
    }

    [McpServerTool(Title = "Atualizar mídia", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true), Description("Atualiza metadados de um item de mídia (título, texto alternativo, legenda, descrição). Só os parâmetros informados são alterados.")]
    public async Task<string> update_media(
        [Description("Id da mídia")] int id,
        [Description("Novo título (opcional)")] string? title = null,
        [Description("Novo texto alternativo (opcional)")] string? alt_text = null,
        [Description("Nova legenda (opcional)")] string? caption = null,
        [Description("Nova descrição (opcional)")] string? description = null,
        CancellationToken ct = default)
    {
        var body = WordPressArgs.Body(
            ("title", WordPressArgs.Str(title)), ("alt_text", WordPressArgs.Str(alt_text)),
            ("caption", WordPressArgs.Str(caption)), ("description", WordPressArgs.Str(description)));
        if (body.Count == 0)
            throw new InvalidOperationException("Informe ao menos um campo para atualizar.");
        return WordPressArgs.Result(await wp.SendAsync(HttpMethod.Post, $"wp/v2/media/{id}", null, body, ct));
    }

    [McpServerTool(Title = "Excluir mídia", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true), Description("Exclui um item de mídia. A mídia não tem lixeira no WordPress: a exclusão é permanente e irreversível, por isso force precisa ser true.")]
    public async Task<string> delete_media(
        [Description("Id da mídia")] int id,
        [Description("Deve ser true para confirmar a exclusão permanente")] bool force = false,
        CancellationToken ct = default)
    {
        if (!force)
            throw new InvalidOperationException("Mídia não tem lixeira: a exclusão é permanente. Chame novamente com force=true para confirmar.");
        return WordPressArgs.Result(await wp.SendAsync(HttpMethod.Delete, $"wp/v2/media/{id}",
            WordPressArgs.Query(("force", true)), null, ct));
    }

    // ---------- Taxonomias ----------

    [McpServerTool(Title = "Listar categorias", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Lista categorias, com busca e paginação. Retorna {total, totalPages, items}.")]
    public Task<string> list_categories(
        [Description("Texto de busca (opcional)")] string? search = null,
        [Description("Id da categoria pai (opcional)")] int? parent = null,
        [Description("Se true, inclui categorias sem posts (padrão true)")] bool include_empty = true,
        [Description("Itens por página (1 a 100, padrão 50)")] int per_page = 50,
        [Description("Número da página (padrão 1)")] int page = 1,
        CancellationToken ct = default) =>
        ListTerms("categories", search, parent, include_empty, per_page, page, ct);

    [McpServerTool(Title = "Criar categoria", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true), Description("Cria uma categoria.")]
    public async Task<string> create_category(
        [Description("Nome")] string name,
        [Description("Slug (opcional)")] string? slug = null,
        [Description("Descrição (opcional)")] string? description = null,
        [Description("Id da categoria pai (opcional)")] int? parent = null,
        CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.SendAsync(HttpMethod.Post, "wp/v2/categories", null, WordPressArgs.Body(
            ("name", WordPressArgs.Str(name)), ("slug", WordPressArgs.Str(slug)),
            ("description", WordPressArgs.Str(description)), ("parent", WordPressArgs.Num(parent))), ct));

    [McpServerTool(Title = "Listar tags", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Lista tags, com busca e paginação. Retorna {total, totalPages, items}.")]
    public Task<string> list_tags(
        [Description("Texto de busca (opcional)")] string? search = null,
        [Description("Se true, inclui tags sem posts (padrão true)")] bool include_empty = true,
        [Description("Itens por página (1 a 100, padrão 50)")] int per_page = 50,
        [Description("Número da página (padrão 1)")] int page = 1,
        CancellationToken ct = default) =>
        ListTerms("tags", search, null, include_empty, per_page, page, ct);

    [McpServerTool(Title = "Criar tag", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true), Description("Cria uma tag.")]
    public async Task<string> create_tag(
        [Description("Nome")] string name,
        [Description("Slug (opcional)")] string? slug = null,
        [Description("Descrição (opcional)")] string? description = null,
        CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.SendAsync(HttpMethod.Post, "wp/v2/tags", null, WordPressArgs.Body(
            ("name", WordPressArgs.Str(name)), ("slug", WordPressArgs.Str(slug)),
            ("description", WordPressArgs.Str(description))), ct));

    // ---------- Comentários ----------

    [McpServerTool(Title = "Listar comentários", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Lista comentários, com filtros e paginação. Retorna {total, totalPages, items}. Para ver pendentes use status='hold'.")]
    public async Task<string> list_comments(
        [Description("Id do post (opcional)")] int? post = null,
        [Description("Status: approve, hold (pendente), spam, trash ou 'all' (opcional)")] string? status = null,
        [Description("Texto de busca (opcional)")] string? search = null,
        [Description("Itens por página (1 a 100, padrão 10)")] int per_page = 10,
        [Description("Número da página (padrão 1)")] int page = 1,
        CancellationToken ct = default)
    {
        var query = WordPressArgs.Query(
            ("context", "edit"), ("post", post), ("status", status), ("search", search),
            ("per_page", Math.Clamp(per_page, 1, 100)), ("page", Math.Max(page, 1)));
        return WordPressArgs.ListResult(await wp.GetAsync("wp/v2/comments", query, ct));
    }

    [McpServerTool(Title = "Criar comentário", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true), Description("Cria um comentário em um post, em nome do usuário autenticado (ou como resposta a outro comentário).")]
    public async Task<string> create_comment(
        [Description("Id do post")] int post,
        [Description("Conteúdo do comentário")] string content,
        [Description("Id do comentário pai, para respostas (opcional)")] int? parent = null,
        CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.SendAsync(HttpMethod.Post, "wp/v2/comments", null, WordPressArgs.Body(
            ("post", WordPressArgs.Num(post)), ("content", WordPressArgs.Str(content)),
            ("parent", WordPressArgs.Num(parent))), ct));

    [McpServerTool(Title = "Atualizar/moderar comentário", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true), Description("Atualiza um comentário: edita o conteúdo e/ou muda o status de moderação (approved/approve, hold, spam, trash).")]
    public async Task<string> update_comment(
        [Description("Id do comentário")] int id,
        [Description("Novo status: approved, hold, spam ou trash (opcional)")] string? status = null,
        [Description("Novo conteúdo (opcional)")] string? content = null,
        CancellationToken ct = default)
    {
        var body = WordPressArgs.Body(("status", WordPressArgs.Str(status)), ("content", WordPressArgs.Str(content)));
        if (body.Count == 0)
            throw new InvalidOperationException("Informe status e/ou content.");
        return WordPressArgs.Result(await wp.SendAsync(HttpMethod.Post, $"wp/v2/comments/{id}", null, body, ct));
    }

    [McpServerTool(Title = "Excluir comentário", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true), Description("Move um comentário para a lixeira (padrão) ou o exclui permanentemente (force=true, irreversível).")]
    public async Task<string> delete_comment(
        [Description("Id do comentário")] int id,
        [Description("true = exclusão permanente e irreversível; false (padrão) = lixeira")] bool force = false,
        CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.SendAsync(HttpMethod.Delete, $"wp/v2/comments/{id}",
            WordPressArgs.Query(("force", force)), null, ct));

    // ---------- Usuários ----------

    [McpServerTool(Title = "Listar usuários", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Lista usuários do site, com busca e paginação. Retorna {total, totalPages, items}. Exige credenciais com permissão para listar usuários.")]
    public async Task<string> list_users(
        [Description("Texto de busca (opcional)")] string? search = null,
        [Description("Papel: administrator, editor, author, contributor, subscriber... (opcional)")] string? roles = null,
        [Description("Itens por página (1 a 100, padrão 10)")] int per_page = 10,
        [Description("Número da página (padrão 1)")] int page = 1,
        CancellationToken ct = default)
    {
        var query = WordPressArgs.Query(
            ("context", "edit"), ("search", search), ("roles", roles),
            ("per_page", Math.Clamp(per_page, 1, 100)), ("page", Math.Max(page, 1)));
        return WordPressArgs.ListResult(await wp.GetAsync("wp/v2/users", query, ct));
    }

    [McpServerTool(Title = "Obter usuário", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Retorna um usuário pelo id.")]
    public async Task<string> get_user(
        [Description("Id do usuário")] int id,
        CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.GetAsync($"wp/v2/users/{id}", WordPressArgs.Query(("context", "edit")), ct));

    // ---------- Administração ----------

    [McpServerTool(Title = "Obter configurações", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Retorna as configurações gerais do site (título, descrição, URL, fuso, formato de data, página inicial, comentários...). Exige permissão de administrador.")]
    public async Task<string> get_settings(CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.GetAsync("wp/v2/settings", null, ct));

    [McpServerTool(Title = "Atualizar configurações", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true), Description("Atualiza configurações gerais do site. Informe um objeto JSON só com os campos a alterar (use get_settings para ver os nomes), ex.: {\"title\":\"Novo título\",\"posts_per_page\":12}. Exige permissão de administrador.")]
    public async Task<string> update_settings(
        [Description("Objeto JSON com as configurações a alterar")] string settings_json,
        CancellationToken ct = default)
    {
        var body = WordPressArgs.ParseObject(settings_json, nameof(settings_json));
        if (body is null || body.Count == 0)
            throw new InvalidOperationException("Informe ao menos uma configuração em settings_json.");
        return WordPressArgs.Result(await wp.SendAsync(HttpMethod.Post, "wp/v2/settings", null, body, ct));
    }

    [McpServerTool(Title = "Listar plugins", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Lista os plugins instalados (nome, versão, status active/inactive). Exige permissão de administrador.")]
    public async Task<string> list_plugins(
        [Description("Status: active ou inactive (opcional)")] string? status = null,
        [Description("Texto de busca (opcional)")] string? search = null,
        CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.GetAsync("wp/v2/plugins", WordPressArgs.Query(("status", status), ("search", search)), ct));

    [McpServerTool(Title = "Listar temas", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description("Lista os temas instalados (nome, versão, status). Exige permissão de administrador.")]
    public async Task<string> list_themes(
        [Description("Status: active ou inactive (opcional)")] string? status = null,
        CancellationToken ct = default) =>
        WordPressArgs.Result(await wp.GetAsync("wp/v2/themes", WordPressArgs.Query(("status", status)), ct));

    // ---------- Genérica ----------

    [McpServerTool(Title = "Requisição REST genérica", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true), Description("Faz uma chamada qualquer à REST API do WordPress (/wp-json/...), para rotas sem tool dedicada (WooCommerce, ACF, Yoast, menus, blocos, Abilities em wp-abilities/v1...). 'route' é relativa a /wp-json, ex.: 'wp/v2/menus', 'wc/v3/products', 'wp-abilities/v1/abilities'. GET lê; POST/PUT/PATCH/DELETE alteram dados — confirme com o usuário antes de alterar. Respostas de listas incluem total/totalPages quando o WordPress os informa.")]
    public async Task<string> wp_rest_request(
        [Description("Método HTTP: GET, POST, PUT, PATCH ou DELETE")] string method,
        [Description("Rota relativa a /wp-json, ex.: wp/v2/posts/123")] string route,
        [Description("Objeto JSON com parâmetros de query string (opcional), ex.: {\"per_page\":5,\"context\":\"edit\"}")] string? query_json = null,
        [Description("Objeto JSON com o corpo da requisição (opcional, para POST/PUT/PATCH/DELETE)")] string? body_json = null,
        CancellationToken ct = default)
    {
        var httpMethod = method?.Trim().ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => PatchMethod,
            "DELETE" => HttpMethod.Delete,
            _ => throw new InvalidOperationException("Método inválido: use GET, POST, PUT, PATCH ou DELETE.")
        };
        if (string.IsNullOrWhiteSpace(route))
            throw new InvalidOperationException("Informe a rota.");

        var query = new Dictionary<string, string?>();
        if (WordPressArgs.ParseObject(query_json, nameof(query_json)) is { } queryObject)
            foreach (var (key, value) in queryObject)
                query[key] = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToJsonString();

        var body = WordPressArgs.ParseObject(body_json, nameof(body_json));
        var response = await wp.SendAsync(httpMethod, route, query, body, ct);

        return response.Total is null && response.TotalPages is null
            ? WordPressArgs.Result(response)
            : WordPressArgs.ListResult(response);
    }

    private async Task<string> ListTerms(string restBase, string? search, int? parent, bool includeEmpty, int perPage, int page, CancellationToken ct)
    {
        var query = WordPressArgs.Query(
            ("search", search), ("parent", parent), ("hide_empty", !includeEmpty),
            ("per_page", Math.Clamp(perPage, 1, 100)), ("page", Math.Max(page, 1)));
        return WordPressArgs.ListResult(await wp.GetAsync($"wp/v2/{restBase}", query, ct));
    }

    private static string GuessMime(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".txt" => "text/plain",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
}
