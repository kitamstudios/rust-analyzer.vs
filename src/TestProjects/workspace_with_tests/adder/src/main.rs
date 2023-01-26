use add_one;

fn main() {
    let num = 10;
    println!("Hello, world! {num} plus one is {}!", add_one::add_one(num));
}

#[cfg(test)]
mod tests {
    use super::*;
    use add_one::*;

    #[test]
    fn it_works_passing() {
        assert_eq!(3, add_one(2));
    }

    #[test]
    fn it_works_failing() {
        assert_eq!(3, add_one(2));
    }

    #[test]
    #[ignore = "this is a test for ignore aka skip"]
    fn it_works_skipped() {
        assert_eq!(3, add_one(2));
    }
}
