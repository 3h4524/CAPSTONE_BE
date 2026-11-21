-- Brand, character and league names a print-on-demand seller must not print without a licence, and the words that
-- describe a counterfeit. The first list (V202611132054__content_moderation.sql) had no ip_trademark terms. They only
-- flag: fan art, parody and a word's everyday meaning ("a marvel") are for the Gemini check to tell apart.
INSERT INTO public.moderation_terms (term, category, severity, match_type, match_unaccented, language, note) VALUES
    -- Entertainment brands and characters.
    ('disney', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('pixar', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('marvel', 'ip_trademark', 'flag', 'word', false, 'en', 'Also an everyday word; Gemini decides.'),
    ('star wars', 'ip_trademark', 'flag', 'phrase', false, 'en', NULL),
    ('harry potter', 'ip_trademark', 'flag', 'phrase', false, 'en', NULL),
    ('mickey mouse', 'ip_trademark', 'flag', 'phrase', false, 'en', NULL),
    ('hello kitty', 'ip_trademark', 'flag', 'phrase', false, 'en', NULL),
    ('pokémon', 'ip_trademark', 'flag', 'word', true, 'en', 'Usually written "pokemon"; the accent-free spelling is not an everyday word.'),
    ('pikachu', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('spongebob', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('barbie', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('minecraft', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    -- Fashion and sports brands.
    ('nike', 'ip_trademark', 'flag', 'word', false, 'en', 'Also the Greek goddess; Gemini decides.'),
    ('adidas', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('gucci', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('louis vuitton', 'ip_trademark', 'flag', 'phrase', false, 'en', NULL),
    ('chanel', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('coca cola', 'ip_trademark', 'flag', 'phrase', false, 'en', 'Also matches "coca-cola": phrases match across any punctuation.'),
    -- Sports leagues.
    ('nfl', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    ('nba', 'ip_trademark', 'flag', 'word', false, 'en', NULL),
    -- Words that describe a counterfeit.
    ('counterfeit', 'illegal', 'flag', 'word', false, 'en', NULL),
    ('knockoff', 'illegal', 'flag', 'word', false, 'en', NULL),
    ('bootleg', 'illegal', 'flag', 'word', false, 'en', 'Also a music or fashion style; Gemini decides.'),
    ('hàng nhái', 'illegal', 'flag', 'phrase', true, 'vi', NULL)
ON CONFLICT (term) DO NOTHING;
